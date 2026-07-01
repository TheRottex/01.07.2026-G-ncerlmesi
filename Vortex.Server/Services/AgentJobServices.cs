using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Vortex.Server.Data;
using Vortex.Shared;

namespace Vortex.Server.Services;

public interface IWorkerAuthenticationService
{
    bool IsWorkerConfigured { get; }
    Task<string?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken);
}

public sealed class WorkerAuthenticationService(IConfiguration configuration, VortexDb db, ILogger<WorkerAuthenticationService> logger) : IWorkerAuthenticationService
{
    private readonly string? _serviceToken = configuration["Worker:ServiceToken"];
    private readonly string? _allowedWorkerId = configuration["Worker:AllowedWorkerId"];

    public bool IsWorkerConfigured => !string.IsNullOrWhiteSpace(_serviceToken) && !string.IsNullOrWhiteSpace(_allowedWorkerId);

    public async Task<string?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!IsWorkerConfigured) return null;
        var workerId = context.Request.Headers["X-Vortex-Worker-Id"].ToString();
        var timestamp = context.Request.Headers["X-Vortex-Timestamp"].ToString();
        var nonce = context.Request.Headers["X-Vortex-Nonce"].ToString();
        var signature = context.Request.Headers["X-Vortex-Signature"].ToString();
        if (!string.Equals(workerId, _allowedWorkerId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(signature)) return null;
        if (!DateTimeOffset.TryParse(timestamp, out var issuedAt) || issuedAt < DateTimeOffset.UtcNow.AddMinutes(-5) || issuedAt > DateTimeOffset.UtcNow.AddMinutes(1)) return null;

        var bodyHash = await ComputeBodyHashAsync(context.Request, cancellationToken);
        var path = context.Request.Path + context.Request.QueryString;
        var canonical = SigningCanonical.Create(context.Request.Method, path, timestamp, nonce, bodyHash);
        var expected = SigningCanonical.Sign(canonical, _serviceToken!);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature))) return null;

        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "DELETE FROM WorkerReplayNonces WHERE ExpiresAt < $now", cancellationToken, ("$now", DateTimeOffset.UtcNow.ToString("O")));
        try
        {
            await VortexDb.ExecuteAsync(connection, "INSERT INTO WorkerReplayNonces (WorkerId, Nonce, Timestamp, ExpiresAt) VALUES ($workerId, $nonce, $timestamp, $expiresAt)", cancellationToken,
                ("$workerId", workerId), ("$nonce", nonce), ("$timestamp", issuedAt.ToString("O")), ("$expiresAt", issuedAt.AddMinutes(10).ToString("O")));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            logger.LogWarning("Rejected replayed worker nonce for {WorkerId}", workerId);
            return null;
        }
        return workerId;
    }

    private static async Task<string> ComputeBodyHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Body.CanSeek) request.Body.Position = 0;
        await using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, cancellationToken);
        if (request.Body.CanSeek) request.Body.Position = 0;
        return SigningCanonical.Hash(memory.ToArray());
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
    private static readonly string[] TerminalStatuses = [AgentJobStatus.Completed.ToString(), AgentJobStatus.Failed.ToString(), AgentJobStatus.Cancelled.ToString(), AgentJobStatus.TimedOut.ToString()];

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
        return await LoadJobAsync(connection, null, "WHERE Id = $id AND UserId = $userId", cancellationToken, ("$id", jobId.ToString()), ("$userId", userId.ToString()));
    }

    public async Task<WorkerReadinessDto> HeartbeatAsync(string workerId, WorkerHeartbeatRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var state = GetReadinessState(request);
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "INSERT INTO WorkerRegistrations (WorkerId, DisplayName, SecretHash, Status, LastHeartbeatAt, HermesReady, ModelReady, StorageHealthy, Message, CreatedAt, UpdatedAt) VALUES ($workerId, $display, NULL, $status, $heartbeat, $hermes, $model, $storage, $message, $createdAt, $updatedAt) ON CONFLICT(WorkerId) DO UPDATE SET Status = $status, LastHeartbeatAt = $heartbeat, HermesReady = $hermes, ModelReady = $model, StorageHealthy = $storage, Message = $message, UpdatedAt = $updatedAt", cancellationToken,
            ("$workerId", workerId), ("$display", workerId), ("$status", state.ToString()), ("$heartbeat", now.ToString("O")), ("$hermes", request.HermesReady ? 1 : 0), ("$model", request.ModelReady ? 1 : 0), ("$storage", request.StorageHealthy ? 1 : 0), ("$message", request.Message), ("$createdAt", now.ToString("O")), ("$updatedAt", now.ToString("O")));
        return new WorkerReadinessDto(workerId, true, request.HermesReady, request.ModelReady, request.StorageHealthy, state, now, request.Message);
    }

    public async Task<WorkerJobLeaseDto?> ClaimNextAsync(string workerId, WorkerClaimRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (!await IsWorkerReadyAsync(connection, workerId, now, cancellationToken)) return null;
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var leaseSeconds = Math.Clamp(request.LeaseSeconds, 15, 300);
        var lease = now.AddSeconds(leaseSeconds);
        var jobId = await AtomicClaimAsync(connection, (SqliteTransaction)tx, workerId, now, lease, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return jobId is null ? null : await LoadLeaseAsync(connection, jobId, cancellationToken);
    }

    public async Task HeartbeatJobAsync(string workerId, Guid jobId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var connection = await db.OpenAsync(cancellationToken);
        await VortexDb.ExecuteAsync(connection, "UPDATE AgentJobs SET Status = $running, LastHeartbeatAt = $now, LeaseExpiresAt = $lease WHERE Id = $id AND WorkerId = $workerId AND Status IN ($claimed, $running) AND LeaseExpiresAt >= $now", cancellationToken,
            ("$running", AgentJobStatus.Running.ToString()), ("$now", now.ToString("O")), ("$lease", now.AddMinutes(2).ToString("O")), ("$id", jobId.ToString()), ("$workerId", workerId), ("$claimed", AgentJobStatus.Claimed.ToString()));
    }

    public async Task<AgentJobStatusDto?> CompleteAsync(string workerId, Guid jobId, WorkerCompleteJobRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var job = await LoadJobAsync(connection, (SqliteTransaction)tx, "WHERE Id = $id AND WorkerId = $workerId", cancellationToken, ("$id", jobId.ToString()), ("$workerId", workerId));
        if (job is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return null;
        }
        if (TerminalStatuses.Contains(job.Status.ToString()))
        {
            await tx.CommitAsync(cancellationToken);
            return job;
        }
        var now = DateTimeOffset.UtcNow;
        if (job.Status is not (AgentJobStatus.Claimed or AgentJobStatus.Running) || job.LeaseExpiresAt is null || job.LeaseExpiresAt < now)
        {
            await tx.RollbackAsync(cancellationToken);
            return null;
        }

        var status = ResolveCompletionStatus(request, job);
        var completedAt = status == AgentJobStatus.Retrying ? null : now.ToString("O");
        var rows = await ExecuteInTransactionAsync(connection, (SqliteTransaction)tx, "UPDATE AgentJobs SET Status = $status, Result = $result, ErrorCode = $error, CompletedAt = $completed, LeaseExpiresAt = NULL WHERE Id = $id AND WorkerId = $workerId AND Status IN ($claimed, $running) AND LeaseExpiresAt >= $now AND Status NOT IN ($completedStatus, $failedStatus, $cancelledStatus, $timedOutStatus)", cancellationToken,
            ("$status", status.ToString()), ("$result", request.Succeeded ? request.Result : null), ("$error", request.Succeeded ? null : request.ErrorCode ?? "WorkerFailed"), ("$completed", completedAt), ("$id", jobId.ToString()), ("$workerId", workerId), ("$claimed", AgentJobStatus.Claimed.ToString()), ("$running", AgentJobStatus.Running.ToString()), ("$now", now.ToString("O")), ("$completedStatus", AgentJobStatus.Completed.ToString()), ("$failedStatus", AgentJobStatus.Failed.ToString()), ("$cancelledStatus", AgentJobStatus.Cancelled.ToString()), ("$timedOutStatus", AgentJobStatus.TimedOut.ToString()));
        if (rows != 1)
        {
            await tx.RollbackAsync(cancellationToken);
            return null;
        }

        var executionStatus = status == AgentJobStatus.Completed ? AgentExecutionStatus.Succeeded : status == AgentJobStatus.TimedOut ? AgentExecutionStatus.TimedOut : AgentExecutionStatus.Failed;
        await ExecuteInTransactionAsync(connection, (SqliteTransaction)tx, "UPDATE AgentExecutionLogs SET Status = $status, ErrorCode = $error, FinishedAt = $finished WHERE UserId = $userId AND RequestId = $requestId", cancellationToken,
            ("$status", executionStatus.ToString()), ("$error", request.ErrorCode), ("$finished", now.ToString("O")), ("$userId", job.UserId.ToString()), ("$requestId", job.RequestId));
        if (status == AgentJobStatus.Completed)
        {
            var today = DateOnly.FromDateTime(now.UtcDateTime).ToString("O");
            await ExecuteInTransactionAsync(connection, (SqliteTransaction)tx, "INSERT INTO AgentUsageCounters (Id, UserId, Date, AgentRuns, InputTokens, OutputTokens, EstimatedCost, UpdatedAt) VALUES ($id, $userId, $date, 1, $input, $output, 0, $updatedAt) ON CONFLICT(UserId, Date) DO UPDATE SET AgentRuns = AgentRuns + 1, InputTokens = InputTokens + $input, OutputTokens = OutputTokens + $output, UpdatedAt = $updatedAt", cancellationToken,
                ("$id", Guid.NewGuid().ToString()), ("$userId", job.UserId.ToString()), ("$date", today), ("$input", request.InputTokens), ("$output", request.OutputTokens), ("$updatedAt", now.ToString("O")));
        }
        await ExecuteInTransactionAsync(connection, (SqliteTransaction)tx, "INSERT INTO AuditLogs (Id, UserId, Action, Details, CreatedAt, CorrelationId) VALUES ($id, $userId, 'AgentJobCompleted', $details, $createdAt, $correlationId)", cancellationToken,
            ("$id", Guid.NewGuid().ToString()), ("$userId", job.UserId.ToString()), ("$details", $"job={jobId};status={status};worker={workerId};resultLength={(request.Result ?? string.Empty).Length}"), ("$createdAt", now.ToString("O")), ("$correlationId", job.RequestId));
        await tx.CommitAsync(cancellationToken);
        return await LoadJobAsync(connection, null, "WHERE Id = $id", cancellationToken, ("$id", jobId.ToString()));
    }

    private async Task<AgentJobStatusDto?> GetByIdempotencyAsync(Guid userId, string key, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await LoadJobAsync(connection, null, "WHERE UserId = $userId AND IdempotencyKey = $key", cancellationToken, ("$userId", userId.ToString()), ("$key", key));
    }

    private static WorkerReadinessState GetReadinessState(WorkerHeartbeatRequest request)
    {
        if (!request.HermesReady && !request.ModelReady && request.Message?.Contains("yapılandır", StringComparison.OrdinalIgnoreCase) == true) return WorkerReadinessState.NotConfigured;
        if (!request.StorageHealthy) return WorkerReadinessState.StorageDegraded;
        if (request.HermesReady && request.ModelReady) return WorkerReadinessState.ModelReady;
        if (request.HermesReady) return WorkerReadinessState.HermesReady;
        return WorkerReadinessState.Connected;
    }

    private static async Task<bool> IsWorkerReadyAsync(SqliteConnection connection, string workerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT LastHeartbeatAt, HermesReady, ModelReady, StorageHealthy, Status FROM WorkerRegistrations WHERE WorkerId = $workerId";
        command.Parameters.AddWithValue("$workerId", workerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return false;
        var heartbeat = reader.IsDBNull(0) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(0));
        return heartbeat is not null && heartbeat >= now.AddMinutes(-2) && reader.GetInt32(1) == 1 && reader.GetInt32(2) == 1 && reader.GetInt32(3) == 1 && reader.GetString(4) != WorkerReadinessState.NotConfigured.ToString();
    }

    private static async Task<string?> AtomicClaimAsync(SqliteConnection connection, SqliteTransaction transaction, string workerId, DateTimeOffset now, DateTimeOffset lease, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
UPDATE AgentJobs
SET Status = $claimed,
    WorkerId = $workerId,
    ClaimedAt = COALESCE(ClaimedAt, $now),
    StartedAt = COALESCE(StartedAt, $now),
    LeaseExpiresAt = $lease,
    AttemptCount = AttemptCount + 1,
    LastHeartbeatAt = $now
WHERE Id = (
    SELECT Id FROM AgentJobs
    WHERE CancellationRequested = 0
      AND AttemptCount < MaxAttempts
      AND (
        Status IN ($pending, $queued, $retrying, $workerUnavailable)
        OR (Status IN ($claimed, $running) AND LeaseExpiresAt IS NOT NULL AND LeaseExpiresAt < $now)
      )
    ORDER BY Priority DESC, CreatedAt ASC
    LIMIT 1
)
  AND CancellationRequested = 0
  AND AttemptCount < MaxAttempts
  AND (
    Status IN ($pending, $queued, $retrying, $workerUnavailable)
    OR (Status IN ($claimed, $running) AND LeaseExpiresAt IS NOT NULL AND LeaseExpiresAt < $now)
  )
RETURNING Id;
""";
        command.Parameters.AddWithValue("$claimed", AgentJobStatus.Claimed.ToString());
        command.Parameters.AddWithValue("$workerId", workerId);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$lease", lease.ToString("O"));
        command.Parameters.AddWithValue("$pending", AgentJobStatus.Pending.ToString());
        command.Parameters.AddWithValue("$queued", AgentJobStatus.Queued.ToString());
        command.Parameters.AddWithValue("$retrying", AgentJobStatus.Retrying.ToString());
        command.Parameters.AddWithValue("$workerUnavailable", AgentJobStatus.WorkerUnavailable.ToString());
        command.Parameters.AddWithValue("$running", AgentJobStatus.Running.ToString());
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static AgentJobStatus ResolveCompletionStatus(WorkerCompleteJobRequest request, AgentJobStatusDto job)
    {
        if (request.Succeeded) return AgentJobStatus.Completed;
        if (string.Equals(request.ErrorCode, "TimedOut", StringComparison.OrdinalIgnoreCase)) return AgentJobStatus.TimedOut;
        return request.Retryable && job.AttemptCount < job.MaxAttempts ? AgentJobStatus.Retrying : AgentJobStatus.Failed;
    }

    private static async Task<int> ExecuteInTransactionAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task<AgentJobStatusDto?> LoadJobAsync(SqliteConnection connection, SqliteTransaction? transaction, string where, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, UserId, AgentProfileId, ConversationId, RequestId, Status, Priority, CreatedAt, ClaimedAt, StartedAt, CompletedAt, LeaseExpiresAt, AttemptCount, MaxAttempts, ErrorCode, WorkerId, Result, CancellationRequested FROM AgentJobs " + where;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AgentJobStatusDto(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)), reader.GetString(4), Enum.Parse<AgentJobStatus>(reader.GetString(5)), (AgentJobPriority)reader.GetInt32(6), DateTimeOffset.Parse(reader.GetString(7)), reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)), reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)), reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)), reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)), reader.GetInt32(12), reader.GetInt32(13), reader.IsDBNull(14) ? null : reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetString(15), reader.IsDBNull(16) ? null : reader.GetString(16), reader.GetInt32(17) == 1);
    }
}
