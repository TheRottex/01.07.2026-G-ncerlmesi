using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vortex.Server.Data;
using Vortex.Shared;

namespace Vortex.Tests;

public sealed class HermesIsolationIntegrationTests
{
    [Fact]
    public async Task RegisteredUsers_GetIsolatedHermesProfiles_AndWorkerQueue_EnforcesOwnershipAndLimits()
    {
        using var factory = new VortexServerFactory();
        using var client = factory.CreateClient();

        var userA = await RegisterAsync(client, "user-a@vortex.local");
        var userB = await RegisterAsync(client, "user-b@vortex.local");

        using var clientA = factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userA.AccessToken);
        using var clientB = factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userB.AccessToken);
        using var worker = factory.CreateClient();
        await WorkerPostAsync(worker, "/api/worker/heartbeat", new WorkerHeartbeatRequest(true, true, true));

        var statusA = await GetStatusAsync(clientA);
        var statusB = await GetStatusAsync(clientB);

        Assert.NotNull(statusA.Profile);
        Assert.NotNull(statusB.Profile);
        Assert.NotEqual(statusA.Profile!.HermesProfileName, statusB.Profile!.HermesProfileName);
        Assert.NotEqual(statusA.Profile.WorkspaceId, statusB.Profile.WorkspaceId);
        Assert.DoesNotContain("@", statusA.Profile.WorkspaceId ?? string.Empty);
        Assert.Equal(5L * 1024 * 1024 * 1024, statusA.Profile.StorageQuotaBytes);
        Assert.Equal(5, statusA.Policy.DailyAgentRunLimit);
        Assert.Equal(3, statusA.Policy.ActiveScheduledTaskLimit);
        Assert.Equal(25, statusA.Policy.PersistentMemoryLimit);
        Assert.False(statusA.Policy.IsSubAgentEnabled);
        Assert.False(statusA.Policy.IsTerminalEnabled);
        Assert.False(statusA.Policy.IsSystemCommandEnabled);
        Assert.Equal(60, statusA.Policy.MaxRunSeconds);
        Assert.Equal(1, statusA.Policy.MaxConcurrentRuns);

        var remember = await PostChatWithWorkerAsync(clientA, worker, new AgentChatRequest("remember: user-a-private-memory", statusB.Profile.Id), "stored for user A");
        Assert.Equal(statusA.Profile.HermesProfileName, remember.ProfileName);

        var recallB = await PostChatWithWorkerAsync(clientB, worker, new AgentChatRequest("recall", statusA.Profile.Id), "No memory");
        Assert.DoesNotContain("user-a-private-memory", recallB.Response);
        Assert.Equal(statusB.Profile.HermesProfileName, recallB.ProfileName);

        for (var i = 0; i < 4; i++)
        {
            var ok = await PostChatWithWorkerAsync(clientA, worker, new AgentChatRequest($"run {i}", statusB.Profile.Id), $"run {i} done");
            Assert.Equal(statusA.Profile.HermesProfileName, ok.ProfileName);
        }

        var sixth = await clientA.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest("sixth should be rejected", statusB.Profile.Id));
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);

        var postLimitStatusA = await GetStatusAsync(clientA);
        var postLimitStatusB = await GetStatusAsync(clientB);
        Assert.Equal(0, postLimitStatusA.RemainingRunsToday);
        Assert.Equal(4, postLimitStatusB.RemainingRunsToday);
    }

    [Fact]
    public async Task WorkerOffline_KeepsJobQueued_ForOwnerOnlyStatus()
    {
        using var factory = new VortexServerFactory();
        using var client = factory.CreateClient();
        var user = await RegisterAsync(client, "queued@vortex.local");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

        var response = await authed.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest("worker offline"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var queued = (await response.Content.ReadFromJsonAsync<AgentJobStatusDto>(JsonOptions))!;
        Assert.Equal(AgentJobStatus.Queued, queued.Status);

        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/agent/jobs/{queued.JobId}")).StatusCode);
        var ownerStatus = await authed.GetFromJsonAsync<AgentJobStatusDto>($"/api/agent/jobs/{queued.JobId}", JsonOptions);
        Assert.Equal(queued.JobId, ownerStatus!.JobId);
    }

    [Fact]
    public async Task WorkerHmac_BodyTamperAndNonceReplay_AreRejected()
    {
        using var factory = new VortexServerFactory();
        using var worker = factory.CreateClient();
        var ok = await WorkerPostAsync(worker, "/api/worker/heartbeat", new WorkerHeartbeatRequest(true, true, true), nonce: "fixed-nonce-1");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var replay = await WorkerPostAsync(worker, "/api/worker/heartbeat", new WorkerHeartbeatRequest(true, true, true), nonce: "fixed-nonce-1");
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var tampered = await WorkerPostAsync(worker, "/api/worker/heartbeat", new WorkerHeartbeatRequest(true, true, true), signDifferentBody: new WorkerHeartbeatRequest(false, false, false));
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
    }

    [Fact]
    public async Task ConcurrentClaims_OnlyOneWorkerRequestReceivesTheJob()
    {
        using var factory = new VortexServerFactory();
        using var client = factory.CreateClient();
        using var worker = factory.CreateClient();
        var user = await RegisterAsync(client, "claim@vortex.local");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        await WorkerPostAsync(worker, "/api/worker/heartbeat", new WorkerHeartbeatRequest(true, true, true));

        var chatTask = authed.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest("claim once"));
        await Task.Delay(100);
        var claims = await Task.WhenAll(
            WorkerPostAsync(worker, "/api/worker/jobs/claim", new WorkerClaimRequest(1, 60)),
            WorkerPostAsync(worker, "/api/worker/jobs/claim", new WorkerClaimRequest(1, 60)));
        Assert.Equal(1, claims.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, claims.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        _ = await chatTask;
    }

    [Fact]
    public async Task Completion_IsIdempotentAndRejectsWrongOrExpiredWorker()
    {
        using var factory = new VortexServerFactory();
        using var client = factory.CreateClient();
        using var worker = factory.CreateClient();
        var user = await RegisterAsync(client, "complete@vortex.local");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        await WorkerPostAsync(worker, "/api/worker/heartbeat", new WorkerHeartbeatRequest(true, true, true));

        var chatTask = authed.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest("complete once"));
        var lease = await ClaimAsync(worker);
        var wrongWorker = await WorkerPostAsync(worker, $"/api/worker/jobs/{lease.JobId}/complete", new WorkerCompleteJobRequest(true, "wrong"), workerId: "other-worker", token: VortexServerFactory.OtherWorkerToken);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongWorker.StatusCode);

        var complete = await WorkerPostAsync(worker, $"/api/worker/jobs/{lease.JobId}/complete", new WorkerCompleteJobRequest(true, "first", null, false, 3, 4));
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var duplicate = await WorkerPostAsync(worker, $"/api/worker/jobs/{lease.JobId}/complete", new WorkerCompleteJobRequest(true, "second", null, false, 3, 4));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var duplicateJob = (await duplicate.Content.ReadFromJsonAsync<AgentJobStatusDto>(JsonOptions))!;
        Assert.Equal("first", duplicateJob.Result);

        var chat = await chatTask;
        chat.EnsureSuccessStatusCode();
        var status = await GetStatusAsync(authed);
        Assert.Equal(4, status.RemainingRunsToday);

        var chatTask2 = authed.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest("expired lease"));
        var lease2 = await ClaimAsync(worker);
        await ExpireLeaseAsync(factory, lease2.JobId);
        var expired = await WorkerPostAsync(worker, $"/api/worker/jobs/{lease2.JobId}/complete", new WorkerCompleteJobRequest(true, "late", null, false, 1, 1));
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        _ = await chatTask2;
    }

    [Fact]
    public async Task NotReadyWorker_DoesNotClaimJobs()
    {
        using var factory = new VortexServerFactory();
        using var client = factory.CreateClient();
        using var worker = factory.CreateClient();
        var user = await RegisterAsync(client, "not-ready@vortex.local");
        using var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

        await WorkerPostAsync(worker, "/api/worker/heartbeat", new WorkerHeartbeatRequest(false, false, true, "HermesExecutablePath yapılandırılmadı."));
        var chatTask = authed.PostAsJsonAsync("/api/agent/chat", new AgentChatRequest("not ready"));
        await Task.Delay(100);
        var claim = await WorkerPostAsync(worker, "/api/worker/jobs/claim", new WorkerClaimRequest(1, 60));
        Assert.Equal(HttpStatusCode.NoContent, claim.StatusCode);
        _ = await chatTask;
    }

    [Fact]
    public async Task HermesRuntime_TimeoutAndOutputLimits_AreEnforced()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "vortex-worker-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        var script = CreateHermesScript(dataRoot);
        var job = new WorkerJobLeaseDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "req", "workspace", "profile", "input", AgentJobPriority.Normal, DateTimeOffset.UtcNow.AddMinutes(1), 1, 3, 1, "workspace");

        Environment.SetEnvironmentVariable("HERMES_TEST_MODE", "sleep");
        var timeoutRuntime = new global::HermesRuntime(new global::WorkerConfig("http://localhost", "worker", "token", dataRoot, script, 60, 1, 1, 1024, 1024));
        var timedOut = await timeoutRuntime.RunAsync(job, CancellationToken.None);
        Assert.False(timedOut.Succeeded);
        Assert.Equal("TimedOut", timedOut.ErrorCode);

        Environment.SetEnvironmentVariable("HERMES_TEST_MODE", "stdout");
        var outputRuntime = new global::HermesRuntime(new global::WorkerConfig("http://localhost", "worker", "token", dataRoot, script, 60, 1, 1, 128, 1024));
        var limited = await outputRuntime.RunAsync(job with { MaxRunSeconds = 10 }, CancellationToken.None);
        Assert.False(limited.Succeeded);
        Assert.Equal("StdoutLimitExceeded", limited.ErrorCode);
        Environment.SetEnvironmentVariable("HERMES_TEST_MODE", null);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "ChangeMe123!", email));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
    }

    private static async Task<AgentStatusDto> GetStatusAsync(HttpClient client)
    {
        var status = await client.GetFromJsonAsync<AgentStatusDto>("/api/agent/status", JsonOptions);
        return status ?? throw new InvalidOperationException("Agent status boş döndü.");
    }

    private static async Task<AgentChatResponse> PostChatWithWorkerAsync(HttpClient client, HttpClient worker, AgentChatRequest request, string workerResult)
    {
        var chatTask = client.PostAsJsonAsync("/api/agent/chat", request, JsonOptions);
        var lease = await ClaimAsync(worker);
        await WorkerPostAsync(worker, $"/api/worker/jobs/{lease.JobId}/heartbeat", new { });
        var complete = await WorkerPostAsync(worker, $"/api/worker/jobs/{lease.JobId}/complete", new WorkerCompleteJobRequest(true, workerResult, null, false, Math.Max(1, lease.Input.Length / 4), Math.Max(1, workerResult.Length / 4)));
        complete.EnsureSuccessStatusCode();
        var response = await chatTask;
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentChatResponse>(JsonOptions))!;
    }

    private static async Task<WorkerJobLeaseDto> ClaimAsync(HttpClient worker)
    {
        for (var i = 0; i < 30; i++)
        {
            var claim = await WorkerPostAsync(worker, "/api/worker/jobs/claim", new WorkerClaimRequest(1, 60));
            if (claim.StatusCode == HttpStatusCode.OK) return (await claim.Content.ReadFromJsonAsync<WorkerJobLeaseDto>(JsonOptions))!;
            await Task.Delay(50);
        }
        throw new InvalidOperationException("Worker could not claim queued job.");
    }

    private static async Task<HttpResponseMessage> WorkerPostAsync<T>(HttpClient client, string path, T body, string? nonce = null, object? signDifferentBody = null, string? workerId = null, string? token = null)
    {
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        var signedBytes = JsonSerializer.SerializeToUtf8Bytes(signDifferentBody ?? body!, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(bodyBytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        nonce ??= Guid.NewGuid().ToString("N");
        var canonical = SigningCanonical.Create("POST", path, timestamp, nonce, SigningCanonical.Hash(signedBytes));
        request.Headers.Add("X-Vortex-Worker-Id", workerId ?? VortexServerFactory.WorkerId);
        request.Headers.Add("X-Vortex-Timestamp", timestamp);
        request.Headers.Add("X-Vortex-Nonce", nonce);
        request.Headers.Add("X-Vortex-Signature", SigningCanonical.Sign(canonical, token ?? VortexServerFactory.WorkerToken));
        return await client.SendAsync(request);
    }

    private static async Task ExpireLeaseAsync(VortexServerFactory factory, Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VortexDb>();
        await using var connection = await db.OpenAsync();
        await VortexDb.ExecuteAsync(connection, "UPDATE AgentJobs SET LeaseExpiresAt = $expired WHERE Id = $id", CancellationToken.None, ("$expired", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")), ("$id", jobId.ToString()));
    }

    private static string CreateHermesScript(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(directory, "hermes-test.cmd");
            File.WriteAllText(path, "@echo off\r\nif \"%HERMES_TEST_MODE%\"==\"sleep\" (ping -n 6 127.0.0.1 > nul & exit /b 0)\r\nif \"%HERMES_TEST_MODE%\"==\"stdout\" (powershell -NoProfile -Command \"[Console]::Out.Write(('x'*10000))\" & exit /b 0)\r\necho ok\r\n");
            return path;
        }
        else
        {
            var path = Path.Combine(directory, "hermes-test.sh");
            File.WriteAllText(path, "#!/bin/sh\nif [ \"$HERMES_TEST_MODE\" = \"sleep\" ]; then sleep 6; exit 0; fi\nif [ \"$HERMES_TEST_MODE\" = \"stdout\" ]; then python3 - <<'PY'\nprint('x' * 10000, end='')\nPY\nexit 0; fi\necho ok\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class VortexServerFactory : WebApplicationFactory<Program>
{
    public const string WorkerId = "laptop-hermes-test-worker";
    public const string WorkerToken = "integration-worker-token-32-chars-minimum";
    public const string OtherWorkerToken = "other-worker-token-32-chars-minimum";
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "vortex-hermes-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vortex:DataDirectory"] = Path.Combine(_dataRoot, "db"),
                ["Hermes:ProfilesRoot"] = Path.Combine(_dataRoot, "hermes-profiles"),
                ["Hermes:AllowInMemoryTestGateway"] = "true",
                ["Worker:AllowedWorkerId"] = WorkerId,
                ["Worker:ServiceToken"] = WorkerToken,
                ["Worker:ChatWaitSeconds"] = "2",
                ["Jwt:SigningKey"] = "integration-test-signing-key-32-chars-minimum"
            });
        });
    }
}
