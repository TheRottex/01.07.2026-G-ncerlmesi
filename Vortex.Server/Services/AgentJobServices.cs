using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Vortex.Server.Data;
using Vortex.Shared;

namespace Vortex.Server.Services;

public interface IWorkerAuthenticationService
{
    bool IsWorkerConfigured { get; }
    string? Authenticate(HttpContext context);
}

public sealed class WorkerAuthenticationService(IConfiguration configuration, ILogger<WorkerAuthenticationService> logger) : IWorkerAuthenticationService
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> SeenNonces = new();
    private readonly string? _serviceToken = configuration["Worker:ServiceToken"];
    private readonly string? _allowedWorkerId = configuration["Worker:AllowedWorkerId"];

    public bool IsWorkerConfigured => !string.IsNullOrWhiteSpace(_serviceToken) && !string.IsNullOrWhiteSpace(_allowedWorkerId);

    public string? Authenticate(HttpContext context)
    {
        if (!IsWorkerConfigured) return null;
        var workerId = context.Request.Headers["X-Vortex-Worker-Id"].ToString();
        var timestamp = context.Request.Headers["X-Vortex-Timestamp"].ToString();
        var nonce = context.Request.Headers["X-Vortex-Nonce"].ToString();
        var signature = context.Request.Headers["X-Vortex-Signature"].ToString();
        if (!string.Equals(workerId, _allowedWorkerId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(signature)) return null;
        if (!DateTimeOffset.TryParse(timestamp, out var issuedAt) || issuedAt < DateTimeOffset.UtcNow.AddMinutes(-5) || issuedAt > DateTimeOffset.UtcNow.AddMinutes(1)) return null;
        CleanupNonces();
        if (!SeenNonces.TryAdd(workerId + ":" + nonce, DateTimeOffset.UtcNow.AddMinutes(10)))
        {
            logger.LogWarning("Rejected replayed worker nonce for {WorkerId}", workerId);
            return null;
        }
        var path = context.Request.Path + context.Request.QueryString;
        var canonical = string.Join('\n', context.Request.Method.ToUpperInvariant(), path, timestamp, nonce);
        var expected = Sign(canonical, _serviceToken!);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature)) ? workerId : null;
    }

    public static string Sign(string canonical, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return TokenService.Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void CleanupNonces()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in SeenNonces.Where(kv => kv.Value < now).ToArray()) SeenNonces.TryRemove(item.Key, out _);
    }
}

public interface IAgentJobService
{
    Task<AgentJobStatusDto> EnqueueAsync(Guid userId, AgentProfileDto profile, AgentChatRequest request, AgentPolicyDto policy, CancellationToken cancellationToken);
    Task<AgentJobStatusDto?> GetForUserAsync(Guid userId, Guid jobId, CancellationToken cancellationToken);
    Task<WorkerReadinessDto> HeartbeatAsync(string workerId, WorkerHeartbeatRequest request, CancellationToken cancellationToken);
    Task<WorkerJobLeaseDto?> ClaimNextAsync(string workerId, WorkerClaimRequest request, CancellationToken cancellationToken);
    Task HeartbeatJobAsync(string workerId, Guid jobId, CancellationToken cancellationToken);
    Task<AgentJobStatusDto?> CompleteAsync(string workerId, Guid jobId, WorkerCompleteJobRequest request, CancellationToken cancellationToken);
}

public sealed class AgentJobService(VortexDb db, ILogger<AgentJobService> logger) : IAgentJobService
{
    public async Task<AgentJobStatusDto> EnqueueAsync(Guid userId, AgentProfileDto profile, AgentChatRequest request, AgentPolicyDto policy, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (key is not null)
        {
            var existing = await GetByIdempotencyAsync(userId, key, cancellationToken);
            if (existing is not null) return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();
        var requestId = Guid.NewGuid().ToString("N");
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO AgentJobs (Id, UserId, AgentProfileId, ConversationId, RequestId, IdempotencyKey, Status, Priority, Input, CreatedAt, ClaimedAt, StartedAt, CompletedAt, LeaseExpiresAt, AttemptCount, MaxAttempts, ErrorCode, WorkerId, Result, CancellationRequested, LastHeartbeatAt) VALUES ($id, $userId, $profileId, $conversationId, $requestId, $key, $status, $priority, $input, $createdAt, NULL, NULL, NULL, NULL, 0, 3, NULL, NULL, NULL, 0, NULL)", cancellationToken,
            ("$id", jobId.ToString()), ("$userId", userId.ToString()), ("$profileId", profile.Id.ToString()), ("$conversationId", request.ConversationId?.ToString()), ("$requestId", requestId), ("$key", key), ("$status", AgentJobStatus.Queued.ToString()), ("$priority", (int)AgentJobPriority.Normal), ("$input", request.Message), ("$createdAt", now.ToString("O")));
        await VortexDb.ExecuteAsync(connection, "INSERT INTO AgentExecutionLogs (Id, UserId, AgentProfileId, RequestId, StartedAt, FinishedAt, Status, ErrorCode, Model, WasLimitRejected) VALUES ($id, $userId, $profileId, $requestId, $startedAt, NULL, $status, NULL, $model, 0)", cancellationToken,
            ("$id", Guid.NewGuid().ToString()), ("$userId", userId.ToString()), ("$profileId", profile.Id.ToString()), ("$requestId", requestId), ("$startedAt", now.ToString("O")), ("$status", AgentExecutionStatus.Started.ToString()), ("$model", request.Model));
        await VortexDb.ExecuteAsync(connection, "INSERT INTO AuditLogs (Id, UserId, Action, Details, CreatedAt, CorrelationId) VALUES ($id, $userId, 'AgentJobQueued', $details, $createdAt, $correlationId)", cancellationToken,
            ("$id", Guid.NewGuid().ToString()), ("$userId", userId.ToString()), ("$details", $"job={jobId};profile={profile.Id};inputLength={request.Message.Length}"), ("$createdAt", now.ToString("O")), ("$correlationId", requestId));
        logger.LogInformation("Agent job {JobId} queued for user {UserId}", jobId, userId);
        return (await GetForUserAsync(userId, jobId, cancellationToken))!;
    }

    public async Task<AgentJobStatusDto?> GetForUserAsync(Guid userId, Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await LoadJobAsync(connection, "WHERE Id = $id AND UserId = $userId", cancellationToken, ("$id", jobId.ToString()), ("$userId", userId.ToString()));
    }

    public async Task<WorkerReadinessDto> HeartbeatAsync(string workerId, WorkerHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var state = !request.StorageHealthy ? WorkerReadinessState.StorageDegraded : request.HermesReady && request.ModelReady ? WorkerReadinessState.ModelReady : request.HermesReady ? WorkerReadinessState.HermesReady : WorkerReadinessState.Connected;
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO WorkerRegistrations (WorkerId, DisplayName, SecretHash, Status, LastHeartbeatAt, HermesReady, ModelReady, StorageHealthy, Message, CreatedAt, UpdatedAt) VALUES ($workerId, $display, NULL, $status, $heartbeat, $hermes, $model, $storage, $message, $createdAt, $updatedAt) ON CONFLICT(WorkerId) DO UPDATE SET Status = $status, LastHeartbeatAt = $heartbeat, HermesReady = $hermes, ModelReady = $model, StorageHealthy = $storage, Message = $message, UpdatedAt = $updatedAt", cancellationToken,
            ("$workerId", workerId), ("$display", workerId), ("$status", state.ToString()), ("$heartbeat", now.ToString("O")), ("$hermes", request.HermesReady ? 1 : 0), ("$model", request.ModelReady ? 1 : 0), ("$storage", request.StorageHealthy ? 1 : 0), ("$message", request.Message), ("$createdAt", now.ToString("O")), ("$updatedAt", now.ToString("O")));
        return new WorkerReadinessDto(workerId, true, request.HermesReady, request.ModelReady, request.StorageHealthy, state, now, request.Message);
    }

    public async Task<WorkerJobLeaseDto?> ClaimNextAsync(string workerId, WorkerClaimRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var leaseSeconds = Math.Clamp(request.LeaseSeconds, 15, 300);
        var lease = now.AddSeconds(leaseSeconds);
        var jobId = await SelectClaimableJobAsync(connection, tx, now, cancellationToken);
        if (jobId is null)
        {
            await tx.CommitAsync(cancellationToken);
            return null;
        }
        await ExecuteInTransactionAsync(connection, tx, "UPDATE AgentJobs SET Status = $status, WorkerId = $workerId, ClaimedAt = COALESCE(ClaimedAt, $now), StartedAt = COALESCE(StartedAt, $now), LeaseExpiresAt = $lease, AttemptCount = AttemptCount + 1, LastHeartbeatAt = $now WHERE Id = $id", cancellationToken,
            ("$status", AgentJobStatus.Claimed.ToString()), ("$workerId", workerId), ("$now", now.ToString("O")), ("$lease", lease.ToString("O")), ("$id", jobId));
        await tx.CommitAsync(cancellationToken);
        await HeartbeatAsync(workerId, new WorkerHeartbeatRequest(true, true, true), cancellationToken);
        return await LoadLeaseAsync(connection, jobId, cancellationToken);
    }

    public async Task HeartbeatJobAsync(string workerId, Guid jobId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "UPDATE AgentJobs SET Status = $running, LastHeartbeatAt = $now, LeaseExpiresAt = $lease WHERE Id = $id AND WorkerId = $workerId AND Status IN ($claimed, $running)", cancellationToken,
            ("$running", AgentJobStatus.Running.ToString()), ("$now", now.ToString("O")), ("$lease", now.AddMinutes(2).ToString("O")), ("$id", jobId.ToString()), ("$workerId", workerId), ("$claimed", AgentJobStatus.Claimed.ToString()));
    }

    public async Task<AgentJobStatusDto?> CompleteAsync(string workerId, Guid jobId, WorkerCompleteJobRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        var job = await LoadJobAsync(connection, "WHERE Id = $id AND WorkerId = $workerId", cancellationToken, ("$id", jobId.ToString()), ("$workerId", workerId));
        if (job is null) return null;
        var now = DateTimeOffset.UtcNow;
        var status = request.Succeeded ? AgentJobStatus.Completed : request.Retryable && job.AttemptCount < job.MaxAttempts ? AgentJobStatus.Retrying : AgentJobStatus.Failed;
        await VortexDb.ExecuteAsync(connection, "UPDATE AgentJobs SET Status = $status, Result = $result, ErrorCode = $error, CompletedAt = $completed, LeaseExpiresAt = NULL WHERE Id = $id AND WorkerId = $workerId", cancellationToken,
            ("$status", status.ToString()), ("$result", request.Succeeded ? request.Result : null), ("$error", request.Succeeded ? null : request.ErrorCode ?? "WorkerFailed"), ("$completed", status == AgentJobStatus.Retrying ? null : now.ToString("O")), ("$id", jobId.ToString()), ("$workerId", workerId));
        await VortexDb.ExecuteAsync(connection, "UPDATE AgentExecutionLogs SET Status = $status, ErrorCode = $error, FinishedAt = $finished WHERE UserId = $userId AND RequestId = $requestId", cancellationToken,
            ("$status", request.Succeeded ? AgentExecutionStatus.Succeeded.ToString() : AgentExecutionStatus.Failed.ToString()), ("$error", request.ErrorCode), ("$finished", now.ToString("O")), ("$userId", job.UserId.ToString()), ("$requestId", job.RequestId));
        if (request.Succeeded)
        {
            var today = DateOnly.FromDateTime(now.UtcDateTime).ToString("O");
            await VortexDb.ExecuteAsync(connection, "INSERT INTO AgentUsageCounters (Id, UserId, Date, AgentRuns, InputTokens, OutputTokens, EstimatedCost, UpdatedAt) VALUES ($id, $userId, $date, 1, $input, $output, 0, $updatedAt) ON CONFLICT(UserId, Date) DO UPDATE SET AgentRuns = AgentRuns + 1, InputTokens = InputTokens + $input, OutputTokens = OutputTokens + $output, UpdatedAt = $updatedAt", cancellationToken,
                ("$id", Guid.NewGuid().ToString()), ("$userId", job.UserId.ToString()), ("$date", today), ("$input", request.InputTokens), ("$output", request.OutputTokens), ("$updatedAt", now.ToString("O")));
        }
        await VortexDb.ExecuteAsync(connection, "INSERT INTO AuditLogs (Id, UserId, Action, Details, CreatedAt, CorrelationId) VALUES ($id, $userId, 'AgentJobCompleted', $details, $createdAt, $correlationId)", cancellationToken,
            ("$id", Guid.NewGuid().ToString()), ("$userId", job.UserId.ToString()), ("$details", $"job={jobId};status={status};worker={workerId};resultLength={(request.Result ?? string.Empty).Length}"), ("$createdAt", now.ToString("O")), ("$correlationId", job.RequestId));
        return await LoadJobAsync(connection, "WHERE Id = $id", cancellationToken, ("$id", jobId.ToString()));
    }

    private async Task<AgentJobStatusDto?> GetByIdempotencyAsync(Guid userId, string key, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await LoadJobAsync(connection, "WHERE UserId = $userId AND IdempotencyKey = $key", cancellationToken, ("$userId", userId.ToString()), ("$key", key));
    }

    private static async Task<string?> SelectClaimableJobAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT Id FROM AgentJobs WHERE CancellationRequested = 0 AND AttemptCount < MaxAttempts AND (Status IN ('Queued','Pending','Retrying','WorkerUnavailable') OR (Status IN ('Claimed','Running') AND LeaseExpiresAt < $now)) ORDER BY Priority DESC, CreatedAt ASC LIMIT 1";
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecuteInTransactionAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<WorkerJobLeaseDto?> LoadLeaseAsync(SqliteConnection connection, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT j.Id, j.UserId, j.AgentProfileId, j.ConversationId, j.RequestId, COALESCE(p.WorkspaceId, p.HermesProfileName), p.HermesProfileName, j.Input, j.Priority, j.LeaseExpiresAt, j.AttemptCount, j.MaxAttempts, pap.MaxRunSeconds, pap.FileAccessScope FROM AgentJobs j JOIN UserAgentProfiles p ON p.Id = j.AgentProfileId JOIN Users u ON u.Id = j.UserId JOIN PlanAgentPolicies pap ON pap.PlanId = u.PlanId WHERE j.Id = $id";
        command.Parameters.AddWithValue("$id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new WorkerJobLeaseDto(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), (AgentJobPriority)reader.GetInt32(8), DateTimeOffset.Parse(reader.GetString(9)), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetString(13));
    }

    private static async Task<AgentJobStatusDto?> LoadJobAsync(SqliteConnection connection, string where, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, UserId, AgentProfileId, ConversationId, RequestId, Status, Priority, CreatedAt, ClaimedAt, StartedAt, CompletedAt, LeaseExpiresAt, AttemptCount, MaxAttempts, ErrorCode, WorkerId, Result, CancellationRequested FROM AgentJobs " + where;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AgentJobStatusDto(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetString(4), Enum.Parse<AgentJobStatus>(reader.GetString(5)), (AgentJobPriority)reader.GetInt32(6), DateTimeOffset.Parse(reader.GetString(7)), reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)), reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)), reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)), reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)), reader.GetInt32(12), reader.GetInt32(13), reader.IsDBNull(14) ? null : reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetString(15), reader.IsDBNull(16) ? null : reader.GetString(16), reader.GetInt32(17) == 1);
    }
}
