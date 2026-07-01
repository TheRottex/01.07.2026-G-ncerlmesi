using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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

        for (var i = 0; i < 3; i++)
        {
            var taskResponse = await clientA.PostAsJsonAsync("/api/agent/tasks", new CreateAgentTaskRequest($"task-{i}", $"*/{i + 1} * * * *", "Europe/Istanbul"));
            Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);
        }

        var fourthTask = await clientA.PostAsJsonAsync("/api/agent/tasks", new CreateAgentTaskRequest("task-4", "*/5 * * * *", "Europe/Istanbul"));
        Assert.Equal(HttpStatusCode.TooManyRequests, fourthTask.StatusCode);

        var tasksA = await clientA.GetFromJsonAsync<List<AgentTaskDto>>("/api/agent/tasks", JsonOptions) ?? [];
        var tasksB = await clientB.GetFromJsonAsync<List<AgentTaskDto>>("/api/agent/tasks", JsonOptions) ?? [];
        Assert.Equal(3, tasksA.Count);
        Assert.Empty(tasksB);
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
        WorkerJobLeaseDto? lease = null;
        for (var i = 0; i < 20 && lease is null; i++)
        {
            var claim = await WorkerPostAsync(worker, "/api/worker/jobs/claim", new WorkerClaimRequest(1, 60));
            if (claim.StatusCode == HttpStatusCode.OK) lease = await claim.Content.ReadFromJsonAsync<WorkerJobLeaseDto>(JsonOptions);
            else await Task.Delay(50);
        }
        Assert.NotNull(lease);
        await WorkerPostAsync(worker, $"/api/worker/jobs/{lease!.JobId}/heartbeat", new { });
        var complete = await WorkerPostAsync(worker, $"/api/worker/jobs/{lease.JobId}/complete", new WorkerCompleteJobRequest(true, workerResult, null, false, Math.Max(1, lease.Input.Length / 4), Math.Max(1, workerResult.Length / 4)));
        complete.EnsureSuccessStatusCode();
        var response = await chatTask;
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentChatResponse>(JsonOptions))!;
    }

    private static async Task<HttpResponseMessage> WorkerPostAsync<T>(HttpClient client, string path, T body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: JsonOptions) };
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = Guid.NewGuid().ToString("N");
        var canonical = string.Join('\n', "POST", path, timestamp, nonce);
        request.Headers.Add("X-Vortex-Worker-Id", VortexServerFactory.WorkerId);
        request.Headers.Add("X-Vortex-Timestamp", timestamp);
        request.Headers.Add("X-Vortex-Nonce", nonce);
        request.Headers.Add("X-Vortex-Signature", Sign(canonical, VortexServerFactory.WorkerToken));
        return await client.SendAsync(request);
    }

    private static string Sign(string canonical, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed class VortexServerFactory : WebApplicationFactory<Program>
{
    public const string WorkerId = "laptop-hermes-test-worker";
    public const string WorkerToken = "integration-worker-token-32-chars-minimum";
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
