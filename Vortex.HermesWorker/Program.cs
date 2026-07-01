using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vortex.Shared;

var config = WorkerConfig.FromEnvironment(args);
using var http = new HttpClient { BaseAddress = new Uri(config.ServerBaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(90) };
var runtime = new HermesRuntime(config);
Console.WriteLine($"Vortex Hermes Worker started. WorkerId={config.WorkerId}; Server={config.ServerBaseUrl}");

while (!CancellationToken.None.IsCancellationRequested)
{
    try
    {
        var readiness = runtime.GetReadiness();
        await SendAsync(HttpMethod.Post, "api/worker/heartbeat", new WorkerHeartbeatRequest(readiness.HermesReady, readiness.ModelReady, readiness.StorageHealthy, readiness.Message), config, CancellationToken.None);
        var claim = await SendAsync(HttpMethod.Post, "api/worker/jobs/claim", new WorkerClaimRequest(1, config.LeaseSeconds), config, CancellationToken.None);
        if (claim.StatusCode == HttpStatusCode.NoContent)
        {
            await Task.Delay(TimeSpan.FromSeconds(config.IdlePollSeconds));
            continue;
        }
        claim.EnsureSuccessStatusCode();
        var job = await claim.Content.ReadFromJsonAsync<WorkerJobLeaseDto>(WorkerJson.Options) ?? throw new InvalidOperationException("Server boş iş döndürdü.");
        await SendAsync(HttpMethod.Post, $"api/worker/jobs/{job.JobId}/heartbeat", new { }, config, CancellationToken.None);
        var result = await runtime.RunAsync(job, CancellationToken.None);
        await SendAsync(HttpMethod.Post, $"api/worker/jobs/{job.JobId}/complete", result, config, CancellationToken.None);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Worker loop failed: {ex.GetType().Name}: {ex.Message}");
        await Task.Delay(TimeSpan.FromSeconds(config.ErrorDelaySeconds));
    }
}

async Task<HttpResponseMessage> SendAsync<T>(HttpMethod method, string path, T body, WorkerConfig config, CancellationToken cancellationToken)
{
    using var request = new HttpRequestMessage(method, path);
    request.Content = JsonContent.Create(body, options: WorkerJson.Options);
    var timestamp = DateTimeOffset.UtcNow.ToString("O");
    var nonce = Guid.NewGuid().ToString("N");
    var target = "/" + path.TrimStart('/');
    var canonical = string.Join('\n', method.Method.ToUpperInvariant(), target, timestamp, nonce);
    request.Headers.Add("X-Vortex-Worker-Id", config.WorkerId);
    request.Headers.Add("X-Vortex-Timestamp", timestamp);
    request.Headers.Add("X-Vortex-Nonce", nonce);
    request.Headers.Add("X-Vortex-Signature", Sign(canonical, config.ServiceToken));
    return await http.SendAsync(request, cancellationToken);
}

static string Sign(string canonical, string secret)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
}

static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

internal sealed record WorkerReadiness(bool HermesReady, bool ModelReady, bool StorageHealthy, string? Message);

internal sealed class HermesRuntime(WorkerConfig config)
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
        if (!readiness.HermesReady || !readiness.ModelReady || !readiness.StorageHealthy)
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
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"Hermes failed for job {job.JobId}; stderr length={stderr.Length}");
            return new WorkerCompleteJobRequest(false, null, "HermesProcessFailed", true, CountTokens(job.Input), 0);
        }
        return new WorkerCompleteJobRequest(true, stdout.Trim(), null, false, CountTokens(job.Input), CountTokens(stdout));
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

    private static int CountTokens(string? text) => Math.Max(1, (text ?? string.Empty).Length / 4);
}

internal sealed record WorkerConfig(string ServerBaseUrl, string WorkerId, string ServiceToken, string DataRoot, string? HermesExecutablePath, int LeaseSeconds, int IdlePollSeconds, int ErrorDelaySeconds)
{
    public static WorkerConfig FromEnvironment(string[] args)
    {
        var server = Read("VORTEX_SERVER_URL", args, "--server") ?? "https://vortex.example.invalid";
        var workerId = Read("VORTEX_WORKER_ID", args, "--worker-id") ?? "laptop-hermes-worker";
        var token = Read("VORTEX_WORKER_TOKEN", args, "--token") ?? throw new InvalidOperationException("VORTEX_WORKER_TOKEN zorunludur.");
        var dataRoot = Read("VORTEX_WORKER_DATA", args, "--data") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VortexAI", "worker-data");
        var hermes = Read("HERMES_EXECUTABLE_PATH", args, "--hermes");
        Directory.CreateDirectory(dataRoot);
        return new WorkerConfig(server, workerId, token, dataRoot, hermes, 60, 3, 10);
    }

    private static string? Read(string env, string[] args, string flag)
    {
        var value = Environment.GetEnvironmentVariable(env);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        for (var i = 0; i < args.Length - 1; i++) if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}

internal static class WorkerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
