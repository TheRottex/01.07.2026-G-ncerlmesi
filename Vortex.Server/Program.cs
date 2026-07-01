using System.Security.Claims;
using System.Text.Json;
using Vortex.Server.Data;
using Vortex.Server.Services;
using Vortex.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<VortexDb>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IAgentIsolationService, AgentIsolationService>();
if (builder.Configuration.GetValue<bool>("Hermes:AllowInMemoryTestGateway", false)) builder.Services.AddSingleton<IHermesGatewayService, FakeHermesGatewayService>();
else builder.Services.AddSingleton<IHermesGatewayService, HermesGatewayService>();
builder.Services.AddSingleton<IWorkerAuthenticationService, WorkerAuthenticationService>();
builder.Services.AddScoped<IHermesProfileService, HermesProfileService>();
builder.Services.AddScoped<IAgentPolicyService, AgentPolicyService>();
builder.Services.AddScoped<IAgentUsageService, AgentUsageService>();
builder.Services.AddScoped<IAgentJobService, AgentJobService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<DesktopAuthService>();
builder.Services.AddScoped<ModelRouter>();
builder.Services.AddScoped<AiProviderClient>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddHttpClient("ai-provider", client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();
app.UseCors();
await app.Services.GetRequiredService<VortexDb>().InitializeAsync();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Vortex.Server", utc = DateTimeOffset.UtcNow }));

app.MapGet("/health/worker", async (VortexDb db, CancellationToken ct) =>
{
    await using var connection = await db.OpenAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT WorkerId, LastHeartbeatAt, HermesReady, ModelReady, StorageHealthy, Status, Message FROM WorkerRegistrations ORDER BY UpdatedAt DESC LIMIT 1";
    await using var reader = await command.ExecuteReaderAsync(ct);
    if (!await reader.ReadAsync(ct)) return Results.Ok(new { serverHealthy = true, workerConnected = false, hermesReady = false, modelReady = false, storageHealthy = false });
    var heartbeat = reader.IsDBNull(1) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(1));
    var connected = heartbeat is not null && heartbeat > DateTimeOffset.UtcNow.AddMinutes(-2);
    return Results.Ok(new { serverHealthy = true, workerId = reader.GetString(0), workerConnected = connected, hermesReady = connected && reader.GetInt32(2) == 1, modelReady = connected && reader.GetInt32(3) == 1, storageHealthy = connected && reader.GetInt32(4) == 1, status = connected ? reader.GetString(5) : "Offline", message = reader.IsDBNull(6) ? null : reader.GetString(6) });
});

app.MapPost("/api/auth/register", async (RegisterRequest request, AuthService auth, CancellationToken ct) =>
{
    try { return Results.Ok(await auth.RegisterAsync(request, ct)); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapPost("/api/auth/login", async (LoginRequest request, AuthService auth, CancellationToken ct) =>
{
    try { return Results.Ok(await auth.LoginAsync(request, ct)); }
    catch { return Results.Unauthorized(); }
});

app.MapPost("/api/web/auth/register", async (WebRegisterRequest request, AuthService auth, CancellationToken ct) =>
{
    try { return Results.Ok(await auth.RegisterWebUserAsync(request, ct)); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapPost("/api/web/auth/login", async (WebLoginRequest request, AuthService auth, CancellationToken ct) =>
{
    try { return Results.Ok(await auth.LoginAsync(new LoginRequest(request.Email, request.Password), ct)); }
    catch { return Results.Unauthorized(); }
});

app.MapPost("/api/desktop-auth/sessions", async (StartDesktopAuthRequest request, DesktopAuthService desktopAuth, CancellationToken ct) =>
{
    try { return Results.Ok(await desktopAuth.StartAsync(request, ct)); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapGet("/api/desktop-auth/sessions/{id:guid}", async (Guid id, DesktopAuthService desktopAuth, CancellationToken ct) =>
{
    try { return Results.Ok(await desktopAuth.GetStatusAsync(id, ct)); }
    catch { return Results.NotFound(); }
});

app.MapPost("/api/desktop-auth/sessions/{id:guid}/complete", async (HttpContext context, Guid id, CompleteDesktopAuthRequest request, DesktopAuthService desktopAuth, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    try { return Results.Ok(await desktopAuth.CompleteAsync(id, userId.Value, request.State, ct)); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapPost("/api/desktop-auth/token", async (ExchangeDesktopCodeRequest request, DesktopAuthService desktopAuth, CancellationToken ct) =>
{
    try { return Results.Ok(await desktopAuth.ExchangeAsync(request, ct)); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapPost("/api/desktop-auth/sessions/{id:guid}/cancel", async (Guid id, string state, DesktopAuthService desktopAuth, CancellationToken ct) =>
{
    try { await desktopAuth.CancelAsync(id, state, ct); return Results.NoContent(); }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapGet("/api/me", async (HttpContext context, AuthService auth, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    var profile = await auth.GetProfileAsync(userId.Value, ct);
    return profile is null ? Results.Unauthorized() : Results.Ok(profile);
});

app.MapPost("/api/agent/provision", async (HttpContext context, IHermesProfileService profiles, IAgentPolicyService policies, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await profiles.EnsureProfileAsync(userId.Value, ct);
    return Results.Ok(await policies.GetStatusAsync(userId.Value, ct));
});

app.MapGet("/api/agent/status", async (HttpContext context, IAgentPolicyService policies, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    return userId is null ? Results.Unauthorized() : Results.Ok(await policies.GetStatusAsync(userId.Value, ct));
});

app.MapPost("/api/agent/chat", async (HttpContext context, AgentChatRequest request, IHermesProfileService profiles, IAgentPolicyService policies, IAgentUsageService usage, IAgentJobService jobs, IConfiguration configuration, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Message)) return Results.BadRequest(new { message = "Mesaj boş olamaz." });
    var profile = await profiles.EnsureProfileAsync(userId.Value, ct);
    var policy = await policies.GetPolicyAsync(userId.Value, ct);
    var requestId = Guid.NewGuid().ToString("N");
    if (!await usage.CanStartRunAsync(userId.Value, policy, ct))
    {
        await usage.StartExecutionAsync(userId.Value, profile.Id, requestId, request.Model, true, ct);
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    var job = await jobs.EnqueueAsync(userId.Value, profile, request, policy, ct);
    var waitSeconds = Math.Clamp(configuration.GetValue<int?>("Worker:ChatWaitSeconds") ?? 30, 1, Math.Min(policy.MaxRunSeconds, 300));
    var deadline = DateTimeOffset.UtcNow.AddSeconds(waitSeconds);
    while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
    {
        var current = await jobs.GetForUserAsync(userId.Value, job.JobId, ct);
        if (current is null) return Results.NotFound();
        if (current.Status == AgentJobStatus.Completed)
        {
            var status = await policies.GetStatusAsync(userId.Value, ct);
            return Results.Ok(new AgentChatResponse(current.RequestId, current.Result ?? string.Empty, profile.HermesProfileName, status.RemainingRunsToday, current.JobId));
        }
        if (current.Status is AgentJobStatus.Failed or AgentJobStatus.Cancelled or AgentJobStatus.TimedOut)
        {
            return Results.Problem($"Hermes işi tamamlanamadı: {current.ErrorCode ?? current.Status.ToString()}", statusCode: StatusCodes.Status502BadGateway);
        }
        await Task.Delay(250, ct);
    }
    var queued = await jobs.GetForUserAsync(userId.Value, job.JobId, CancellationToken.None) ?? job;
    return Results.Accepted($"/api/agent/jobs/{queued.JobId}", queued);
});

app.MapGet("/api/agent/jobs/{id:guid}", async (HttpContext context, Guid id, IAgentJobService jobs, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    var job = await jobs.GetForUserAsync(userId.Value, id, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapGet("/api/agent/tasks", async (HttpContext context, VortexDb db, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await using var connection = await db.OpenAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id, Name, Schedule, TimeZone, IsEnabled, CreatedAt FROM AgentScheduledTasks WHERE UserId = $userId ORDER BY CreatedAt DESC";
    command.Parameters.AddWithValue("$userId", userId.Value.ToString());
    var tasks = new List<AgentTaskDto>();
    await using var reader = await command.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct)) tasks.Add(new AgentTaskDto(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4) == 1, DateTimeOffset.Parse(reader.GetString(5))));
    return Results.Ok(tasks);
});

app.MapPost("/api/agent/tasks", async (HttpContext context, CreateAgentTaskRequest request, VortexDb db, IHermesProfileService profiles, IAgentPolicyService policies, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    var status = await policies.GetStatusAsync(userId.Value, ct);
    if (status.ActiveScheduledTaskCount >= status.Policy.ActiveScheduledTaskLimit) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    var profile = await profiles.EnsureProfileAsync(userId.Value, ct);
    string? externalId = null;
    var id = Guid.NewGuid();
    await using var connection = await db.OpenAsync(ct);
    await VortexDb.ExecuteAsync(connection, "INSERT INTO AgentScheduledTasks (Id, UserId, AgentProfileId, ExternalHermesTaskId, Name, Schedule, TimeZone, IsEnabled, CreatedAt) VALUES ($id, $userId, $profileId, $externalId, $name, $schedule, $tz, 1, $createdAt)", ct, ("$id", id.ToString()), ("$userId", userId.Value.ToString()), ("$profileId", profile.Id.ToString()), ("$externalId", externalId), ("$name", request.Name), ("$schedule", request.Schedule), ("$tz", request.TimeZone), ("$createdAt", DateTimeOffset.UtcNow.ToString("O")));
    return Results.Ok(new AgentTaskDto(id, request.Name, request.Schedule, request.TimeZone, true, DateTimeOffset.UtcNow));
});

app.MapDelete("/api/agent/tasks/{id:guid}", async (HttpContext context, Guid id, VortexDb db, IHermesProfileService profiles, IHermesGatewayService gateway, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await using var connection = await db.OpenAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT ExternalHermesTaskId FROM AgentScheduledTasks WHERE Id = $id AND UserId = $userId";
    command.Parameters.AddWithValue("$id", id.ToString());
    command.Parameters.AddWithValue("$userId", userId.Value.ToString());
    var externalId = Convert.ToString(await command.ExecuteScalarAsync(ct));
    if (externalId is null) return Results.NotFound();
    var profile = await profiles.EnsureProfileAsync(userId.Value, ct);
    await gateway.DeleteScheduledTaskAsync(profile, externalId, ct);
    await VortexDb.ExecuteAsync(connection, "DELETE FROM AgentScheduledTasks WHERE Id = $id AND UserId = $userId", ct, ("$id", id.ToString()), ("$userId", userId.Value.ToString()));
    return Results.NoContent();
});

app.MapGet("/api/chats", async (HttpContext context, string? q, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    return userId is null ? Results.Unauthorized() : Results.Ok(await chats.ListSessionsAsync(userId.Value, q, ct));
});

app.MapPost("/api/chats", async (HttpContext context, CreateChatRequest request, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    return userId is null ? Results.Unauthorized() : Results.Ok(await chats.CreateSessionAsync(userId.Value, request.Title, ct));
});

app.MapPut("/api/chats/{id:guid}", async (HttpContext context, Guid id, RenameChatRequest request, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await chats.RenameSessionAsync(userId.Value, id, request.Title, ct);
    return Results.NoContent();
});

app.MapDelete("/api/chats/{id:guid}", async (HttpContext context, Guid id, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await chats.DeleteSessionAsync(userId.Value, id, ct);
    return Results.NoContent();
});

app.MapPost("/api/chat/completions", async (HttpContext context, ChatCompletionRequest request, ChatService chats, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
    context.Response.ContentType = "text/event-stream; charset=utf-8";
    await foreach (var chunk in chats.CompleteAsync(userId.Value, request, ct))
    {
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }
});

app.MapGet("/api/models", async (HttpContext context, VortexDb db, CancellationToken ct) =>
{
    var userId = context.GetUserId();
    if (userId is null) return Results.Unauthorized();
    await using var connection = await db.OpenAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id, ProviderId, Name, DisplayName, IsPremium, SupportsStreaming, SupportsTools, ContextWindowTokens FROM AiModels WHERE IsActive = 1 ORDER BY DisplayName";
    var models = new List<AiModelDto>();
    await using var reader = await command.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct)) models.Add(new AiModelDto(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetInt32(4) == 1, reader.GetInt32(5) == 1, reader.GetInt32(6) == 1, reader.GetInt32(7)));
    return Results.Ok(models);
});

app.MapGet("/api/admin/usage", async (HttpContext context, VortexDb db, CancellationToken ct) =>
{
    if (!context.HasAnyRole(VortexRoles.Administrator, VortexRoles.Owner, VortexRoles.Support)) return Results.Forbid();
    await using var connection = await db.OpenAsync(ct);
    var total = await VortexDb.ScalarLongAsync(connection, "SELECT COUNT(*) FROM UsageRecords", ct);
    var failed = await VortexDb.ScalarLongAsync(connection, "SELECT COUNT(*) FROM UsageRecords WHERE Succeeded = 0", ct);
    return Results.Ok(new { totalRequests = total, failedRequests = failed });
});

app.MapPost("/api/worker/heartbeat", async (HttpContext context, WorkerHeartbeatRequest request, IWorkerAuthenticationService auth, IAgentJobService jobs, CancellationToken ct) =>
{
    var workerId = auth.Authenticate(context);
    if (workerId is null) return Results.Unauthorized();
    return Results.Ok(await jobs.HeartbeatAsync(workerId, request, ct));
});

app.MapPost("/api/worker/jobs/claim", async (HttpContext context, WorkerClaimRequest request, IWorkerAuthenticationService auth, IAgentJobService jobs, CancellationToken ct) =>
{
    var workerId = auth.Authenticate(context);
    if (workerId is null) return Results.Unauthorized();
    var lease = await jobs.ClaimNextAsync(workerId, request, ct);
    return lease is null ? Results.NoContent() : Results.Ok(lease);
});

app.MapPost("/api/worker/jobs/{id:guid}/heartbeat", async (HttpContext context, Guid id, IWorkerAuthenticationService auth, IAgentJobService jobs, CancellationToken ct) =>
{
    var workerId = auth.Authenticate(context);
    if (workerId is null) return Results.Unauthorized();
    await jobs.HeartbeatJobAsync(workerId, id, ct);
    return Results.NoContent();
});

app.MapPost("/api/worker/jobs/{id:guid}/complete", async (HttpContext context, Guid id, WorkerCompleteJobRequest request, IWorkerAuthenticationService auth, IAgentJobService jobs, CancellationToken ct) =>
{
    var workerId = auth.Authenticate(context);
    if (workerId is null) return Results.Unauthorized();
    var job = await jobs.CompleteAsync(workerId, id, request, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.Run();

public static class HttpContextAuthExtensions
{
    public static Guid? GetUserId(this HttpContext context)
    {
        var token = context.Request.Headers.Authorization.ToString();
        if (!token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var tokenService = context.RequestServices.GetRequiredService<TokenService>();
        var principal = tokenService.ValidateToken(token[7..]);
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(id, out var value) ? value : null;
    }

    public static bool HasAnyRole(this HttpContext context, params string[] roles)
    {
        var token = context.Request.Headers.Authorization.ToString();
        if (!token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var tokenService = context.RequestServices.GetRequiredService<TokenService>();
        var principal = tokenService.ValidateToken(token[7..]);
        var role = principal?.FindFirstValue(ClaimTypes.Role);
        return role is not null && roles.Contains(role);
    }
}

public partial class Program { }
