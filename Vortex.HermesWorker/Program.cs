using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vortex.Shared;

var config = WorkerConfig.FromEnvironment(args);
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); };
using var http = new HttpClient { BaseAddress = new Uri(config.ServerBaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(90) };
var runtime = new HermesRuntime(config);
Console.WriteLine($"Vortex Hermes Worker started. WorkerId={config.WorkerId}; Server={config.ServerBaseUrl}");

while (!stopping.IsCancellationRequested)
{
    try
    {
        var readiness = runtime.GetReadiness();
        await SendAsync(HttpMethod.Post, "api/worker/heartbeat", new WorkerHeartbeatRequest(readiness.HermesReady, readiness.ModelReady, readiness.StorageHealthy, readiness.Message), config, stopping.Token);
        if (!readiness.IsReady)
        {
            await Task.Delay(TimeSpan.FromSeconds(config.IdlePollSeconds), stopping.Token);
            continue;
        }

        var claim = await SendAsync(HttpMethod.Post, "api/worker/jobs/claim", new WorkerClaimRequest(1, config.LeaseSeconds), config, stopping.Token);
        if (claim.StatusCode == HttpStatusCode.NoContent)
        {
            await Task.Delay(TimeSpan.FromSeconds(config.IdlePollSeconds), stopping.Token);
            continue;
        }
        claim.EnsureSuccessStatusCode();
        var job = await claim.Content.ReadFromJsonAsync<WorkerJobLeaseDto>(WorkerJson.Options, stopping.Token) ?? throw new InvalidOperationException("Server boş iş döndürdü.");
        await SendAsync(HttpMethod.Post, $"api/worker/jobs/{job.JobId}/heartbeat", new { }, config, stopping.Token);
        using var jobHeartbeat = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        var heartbeatTask = HeartbeatDuringRunAsync(job.JobId, config, jobHeartbeat.Token);
        var result = await runtime.RunAsync(job, stopping.Token);
        await jobHeartbeat.CancelAsync();
        try { await heartbeatTask; } catch (OperationCanceledException) { }
        await SendAsync(HttpMethod.Post, $"api/worker/jobs/{job.JobId}/complete", result, config, stopping.Token);
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Worker loop failed: {ex.GetType().Name}: {ex.Message}");
        await Task.Delay(TimeSpan.FromSeconds(config.ErrorDelaySeconds), stopping.Token);
    }
}

async Task HeartbeatDuringRunAsync(Guid jobId, WorkerConfig config, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, config.LeaseSeconds / 3)), cancellationToken);
        await SendAsync(HttpMethod.Post, $"api/worker/jobs/{jobId}/heartbeat", new { }, config, cancellationToken);
    }
}

async Task<HttpResponseMessage> SendAsync<T>(HttpMethod method, string path, T body, WorkerConfig config, CancellationToken cancellationToken)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(body, WorkerJson.Options);
    using var request = new HttpRequestMessage(method, path);
    request.Content = new ByteArrayContent(bytes);
    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
    var timestamp = DateTimeOffset.UtcNow.ToString("O");
    var nonce = Guid.NewGuid().ToString("N");
    var target = "/" + path.TrimStart('/');
    var bodyHash = SigningCanonical.Hash(bytes);
    var canonical = SigningCanonical.Create(method.Method, target, timestamp, nonce, bodyHash);
    request.Headers.Add("X-Vortex-Worker-Id", config.WorkerId);
    request.Headers.Add("X-Vortex-Timestamp", timestamp);
    request.Headers.Add("X-Vortex-Nonce", nonce);
    request.Headers.Add("X-Vortex-Signature", SigningCanonical.Sign(canonical, config.ServiceToken));
    return await http.SendAsync(request, cancellationToken);
}

public sealed record WorkerReadiness(bool HermesReady, bool ModelReady, bool StorageHealthy, string? Message)
{
    public bool IsReady => HermesReady && ModelReady && StorageHealthy;
}

public sealed class HermesRuntime(WorkerConfig config)
{
    public WorkerReadiness GetReadiness()
    {
        if (string.IsNullOrWhiteSpace(config.HermesExecutablePath)) return new WorkerReadiness(false, false, Directory.Exists(config.DataRoot), "HermesExecutablePath yapılandırılmadı.");
        if (!File.Exists(config.HermesExecutablePath)) return new WorkerReadiness(false, false, Directory.Exists(config.DataRoot), "Hermes executable bulunamadı.");
        return new WorkerReadiness(true, true, Directory.Exists(config.DataRoot), null);
    }

    public async Task<WorkerCompleteJobRequest> RunAsync(WorkerJobLeaseDto job, CancellationToken cancellationToken)
    {
        var readiness = GetReadiness();
        if (!readiness.IsReady)
        {
            return new WorkerCompleteJobRequest(false, null, readiness.Message ?? "WorkerNotReady", true, CountTokens(job.Input), 0);
        }

        var workspace = ResolveWorkspace(job.WorkspaceId);
        Directory.CreateDirectory(workspace);
        using var process = new Process();
        process.StartInfo.FileName = config.HermesExecutablePath;
        process.StartInfo.WorkingDirectory = workspace;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.ArgumentList.Add("agent");
        process.StartInfo.ArgumentList.Add("chat");
        process.StartInfo.ArgumentList.Add("--profile");
        process.StartInfo.ArgumentList.Add(job.HermesProfileName);
        process.StartInfo.ArgumentList.Add("--workspace");
        process.StartInfo.ArgumentList.Add(workspace);
        process.StartInfo.ArgumentList.Add("--message");
        process.StartInfo.ArgumentList.Add(job.Input);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(job.MaxRunSeconds, 1, 86400)));
        try
        {
            process.Start();
            var stdoutTask = ReadBoundedAsync(process.StandardOutput, config.MaxStdoutBytes, process, timeout.Token);
            var stderrTask = ReadBoundedAsync(process.StandardError, config.MaxStderrBytes, process, timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                KillProcessTree(process);
                return new WorkerCompleteJobRequest(false, null, "TimedOut", false, CountTokens(job.Input), 0);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (stdout.Exceeded || stderr.Exceeded)
            {
                KillProcessTree(process);
                return new WorkerCompleteJobRequest(false, null, stdout.Exceeded ? "StdoutLimitExceeded" : "StderrLimitExceeded", false, CountTokens(job.Input), 0);
            }
            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"Hermes failed for job {job.JobId}; stderr length={stderr.Text.Length}");
                return new WorkerCompleteJobRequest(false, null, "HermesProcessFailed", true, CountTokens(job.Input), 0);
            }
            return new WorkerCompleteJobRequest(true, stdout.Text.Trim(), null, false, CountTokens(job.Input), CountTokens(stdout.Text));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            throw;
        }
    }

    private string ResolveWorkspace(string workspaceId)
    {
        if (!PathSafety.IsSafeRelativePath(workspaceId)) throw new InvalidOperationException("Geçersiz workspace kimliği.");
        var root = Path.GetFullPath(Path.Combine(config.DataRoot, "users", workspaceId));
        var workspace = Path.GetFullPath(Path.Combine(root, "workspace"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!workspace.Equals(root, comparison) && !workspace.StartsWith(root + Path.DirectorySeparatorChar, comparison)) throw new InvalidOperationException("Workspace kök dışına çıkıyor.");
        foreach (var child in new[] { "workspace", "memory", "automations", "artifacts", "temp", "metadata" }) Directory.CreateDirectory(Path.Combine(root, child));
        return workspace;
    }

    private static async Task<BoundedReadResult> ReadBoundedAsync(StreamReader reader, int maxChars, Process process, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maxChars, 4096));
        var buffer = new char[Math.Min(4096, Math.Max(1, maxChars))];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) return new BoundedReadResult(builder.ToString(), false);
            if (builder.Length + read > maxChars)
            {
                var allowed = Math.Max(0, maxChars - builder.Length);
                if (allowed > 0) builder.Append(buffer, 0, allowed);
                KillProcessTree(process);
                return new BoundedReadResult(builder.ToString(), true);
            }
            builder.Append(buffer, 0, read);
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static int CountTokens(string? text) => Math.Max(1, (text ?? string.Empty).Length / 4);
}

public sealed record WorkerConfig(string ServerBaseUrl, string WorkerId, string ServiceToken, string DataRoot, string? HermesExecutablePath, int LeaseSeconds, int IdlePollSeconds, int ErrorDelaySeconds, int MaxStdoutBytes, int MaxStderrBytes)
{
    public static WorkerConfig FromEnvironment(string[] args)
    {
        var server = Read("VORTEX_SERVER_URL", args, "--server") ?? "https://vortex.example.invalid";
        var workerId = Read("VORTEX_WORKER_ID", args, "--worker-id") ?? "laptop-hermes-worker";
        var token = Read("VORTEX_WORKER_TOKEN", args, "--token") ?? throw new InvalidOperationException("VORTEX_WORKER_TOKEN zorunludur.");
        var dataRoot = Read("VORTEX_WORKER_DATA", args, "--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VortexAI", "worker-data");
        var hermes = Read("HERMES_EXECUTABLE_PATH", args, "--hermes");
        var maxStdout = ReadInt("Worker:MaxStdoutBytes", "VORTEX_WORKER_MAX_STDOUT_BYTES", args, "--max-stdout-bytes", 65536);
        var maxStderr = ReadInt("Worker:MaxStderrBytes", "VORTEX_WORKER_MAX_STDERR_BYTES", args, "--max-stderr-bytes", 32768);
        Directory.CreateDirectory(dataRoot);
        return new WorkerConfig(server, workerId, token, dataRoot, hermes, 60, 3, 10, maxStdout, maxStderr);
    }

    private static string? Read(string env, string[] args, string flag)
    {
        var value = Environment.GetEnvironmentVariable(env);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        for (var i = 0; i < args.Length - 1; i++) if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static int ReadInt(string configName, string env, string[] args, string flag, int fallback)
    {
        var value = Read(env, args, flag) ?? Environment.GetEnvironmentVariable(configName);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}

public sealed record BoundedReadResult(string Text, bool Exceeded);

internal static class WorkerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
