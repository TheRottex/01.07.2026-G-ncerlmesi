using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Vortex.Shared;

public static class VortexRoles
{
    public const string User = "User";
    public const string Support = "Support";
    public const string Administrator = "Administrator";
    public const string Owner = "Owner";
    public static readonly string[] All = [User, Support, Administrator, Owner];
}

public static class VortexFeatures
{
    public const string Chat = "chat";
    public const string PremiumChat = "premium-chat";
    public const string FileContext = "file-context";
    public const string ProjectContext = "project-context";
    public const string VoiceInput = "voice-input";
    public const string TextToSpeech = "text-to-speech";
    public const string LocalTools = "local-tools";
    public const string HermesAgent = "hermes-agent";
}

public static class SupportedFileTypes
{
    public static readonly string[] Extensions =
    [
        ".cs", ".axaml", ".xaml", ".json", ".md", ".txt", ".xml", ".yaml", ".yml",
        ".js", ".ts", ".html", ".css", ".py", ".cpp", ".h", ".ps1", ".bat"
    ];
}

public enum ChatRole { System, User, Assistant, Tool }
public enum LocalToolRiskLevel { Low, Medium, High, Critical }
public enum HermesProfileStatus { Provisioning, Ready, ProvisioningFailed, Disabled }
public enum AgentExecutionStatus { Started, Succeeded, Failed, LimitRejected, TimedOut, Cancelled }
public enum AgentJobStatus { Pending, Queued, Claimed, Running, WaitingForApproval, Completed, Failed, Cancelled, TimedOut, WorkerUnavailable, Retrying }
public enum AgentJobPriority { Low = 0, Normal = 100, High = 200 }
public enum WorkerReadinessState { Offline, Connected, HermesReady, ModelReady, StorageDegraded, NotConfigured }

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserProfileDto User);
public sealed record UserProfileDto(Guid Id, string Email, string DisplayName, string Role, string PlanName, long StorageQuotaBytes, long StorageUsedBytes);

public sealed record ChatSessionDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CreateChatRequest(string? Title = null);
public sealed record RenameChatRequest(string Title);
public sealed record ChatMessageDto(Guid Id, Guid ChatSessionId, string Role, string Content, DateTimeOffset CreatedAt, string? ModelName, bool IsStreaming, string? ErrorMessage);
public sealed record AiChatMessage(string Role, string Content);
public sealed record ChatCompletionRequest(Guid? ChatSessionId, string Message, string? RequestedModel, string? SystemPrompt, IReadOnlyList<AttachedFileDto>? Files, bool Stream = true);
public sealed record ChatCompletionChunk(string Delta, bool IsFinal, string? ModelName = null, string? ErrorMessage = null, string? CorrelationId = null);
public sealed record AttachedFileDto(Guid Id, string FileName, string ContentType, long SizeBytes, string? ExtractedText = null);

public sealed record AiProviderDto(Guid Id, string Name, string Type, string BaseUrl, bool IsActive, int Priority);
public sealed record AiModelDto(Guid Id, Guid ProviderId, string Name, string DisplayName, bool IsPremium, bool SupportsStreaming, bool SupportsTools, int ContextWindowTokens);
public sealed record SubscriptionPlanDto(Guid Id, string Name, string DisplayName, long StorageQuotaBytes, int DailyRequestLimit, int MonthlyRequestLimit, bool IsActive);
public sealed record PlanModelPolicyDto(Guid Id, Guid PlanId, Guid ProviderId, Guid ModelId, int Priority, int DailyUsageLimit, int MonthlyUsageLimit, string FeatureName, Guid? FallbackProviderId, Guid? FallbackModelId, bool IsActive);
public sealed record FeatureEntitlementDto(Guid Id, Guid PlanId, string FeatureName, bool IsEnabled, int? Limit, bool RequiresConfirmation);
public sealed record StorageQuotaDto(long QuotaBytes, long UsedBytes, long CommittedQuotaBytes, long AvailablePhysicalBytes);

public sealed record AgentPolicyDto(
    int DailyAgentRunLimit,
    int ActiveScheduledTaskLimit,
    int PersistentMemoryLimit,
    bool IsSubAgentEnabled,
    bool IsTerminalEnabled,
    bool IsSystemCommandEnabled,
    int MaxRunSeconds,
    int MaxConcurrentRuns,
    string FileAccessScope);

public sealed record AgentProfileDto(Guid Id, Guid UserId, string HermesProfileName, string HermesHomePath, string Status, DateTimeOffset CreatedAt, DateTimeOffset? LastStartedAt, string? WorkerId = null, string? WorkspaceId = null, long StorageQuotaBytes = 0, long StorageUsedBytes = 0);
public sealed record AgentUsageDto(DateOnly Date, int AgentRuns, int InputTokens, int OutputTokens, decimal EstimatedCost, DateTimeOffset UpdatedAt);
public sealed record AgentStatusDto(AgentProfileDto? Profile, AgentPolicyDto Policy, AgentUsageDto Usage, int RemainingRunsToday, int ActiveScheduledTaskCount, int RemainingScheduledTasks, WorkerReadinessDto? Worker = null);
public sealed record AgentChatRequest(string Message, Guid? RequestedProfileId = null, string? Model = null, Guid? ConversationId = null, string? IdempotencyKey = null);
public sealed record AgentChatResponse(string RequestId, string Response, string ProfileName, int RemainingRunsToday, Guid? JobId = null);
public sealed record AgentTaskDto(Guid Id, string Name, string Schedule, string TimeZone, bool IsEnabled, DateTimeOffset CreatedAt);
public sealed record CreateAgentTaskRequest(string Name, string Schedule, string TimeZone);
public sealed record WorkerReadinessDto(string WorkerId, bool WorkerConnected, bool HermesReady, bool ModelReady, bool StorageHealthy, WorkerReadinessState State, DateTimeOffset UpdatedAt, string? Message = null);
public sealed record AgentJobStatusDto(Guid JobId, Guid UserId, Guid AgentProfileId, Guid? ConversationId, string RequestId, AgentJobStatus Status, AgentJobPriority Priority, DateTimeOffset CreatedAt, DateTimeOffset? ClaimedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? LeaseExpiresAt, int AttemptCount, int MaxAttempts, string? ErrorCode, string? WorkerId, string? Result, bool CancellationRequested);
public sealed record WorkerClaimRequest(int MaxJobs = 1, int LeaseSeconds = 60);
public sealed record WorkerJobLeaseDto(Guid JobId, Guid UserId, Guid AgentProfileId, Guid? ConversationId, string RequestId, string WorkspaceId, string HermesProfileName, string Input, AgentJobPriority Priority, DateTimeOffset LeaseExpiresAt, int AttemptCount, int MaxAttempts, int MaxRunSeconds, string FileAccessScope);
public sealed record WorkerHeartbeatRequest(bool HermesReady, bool ModelReady, bool StorageHealthy, string? Message = null);
public sealed record WorkerCompleteJobRequest(bool Succeeded, string? Result, string? ErrorCode = null, bool Retryable = false, int InputTokens = 0, int OutputTokens = 0);

public sealed record StartDesktopAuthRequest(string StateHash, string CodeChallenge, string CallbackUri);
public sealed record StartDesktopAuthResponse(Guid SessionId, string AuthorizationUrl, DateTimeOffset ExpiresAt);
public sealed record CompleteDesktopAuthRequest(Guid SessionId, string State);
public sealed record CompleteDesktopAuthResponse(string CallbackUrl, string Message);
public sealed record ExchangeDesktopCodeRequest(Guid SessionId, string Code, string CodeVerifier, string State);
public sealed record DesktopAuthStatusResponse(Guid SessionId, bool Completed, bool Consumed, DateTimeOffset ExpiresAt);
public sealed record WebRegisterRequest(string Email, string Password, string DisplayName, bool AcceptTerms);
public sealed record WebLoginRequest(string Email, string Password, bool RememberMe);

public sealed record LocalAgentHello(string AgentName, string Version, string Platform, IReadOnlyList<LocalToolDescriptor> Tools);
public sealed record LocalToolDescriptor(string Name, string Description, bool IsEnabled, bool RequiresConfirmation, LocalToolRiskLevel RiskLevel);
public sealed record LocalToolRequest(string RequestId, string ToolName, Dictionary<string, string> Arguments, DateTimeOffset ExpiresAt, string Signature, bool UserConfirmed);
public sealed record LocalToolResponse(string RequestId, bool Succeeded, string Message, string? Output = null);

public sealed record AudioDeviceDto(string Id, string Name, bool IsDefaultInput, bool IsDefaultOutput);
public sealed record SpeechToTextRequest(string AudioBase64, string ContentType, string? Language);
public sealed record SpeechToTextResponse(string Text, decimal Confidence);
public sealed record TextToSpeechRequest(string Text, string? Voice, string? Language);
public sealed record TextToSpeechResponse(bool Succeeded, string Message);

public static class SigningCanonical
{
    public static string Create(string method, string path, string timestamp, string nonce, string bodyHash)
        => string.Join('\n', method.ToUpperInvariant(), path, timestamp, nonce, bodyHash);

    public static string Hash(byte[] bytes) => TokenServiceCompatibleBase64Url(SHA256.HashData(bytes));

    public static string Sign(string canonical, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return TokenServiceCompatibleBase64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string TokenServiceCompatibleBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class SecretMasker
{
    public static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length <= 8) return "••••";
        return trimmed[..Math.Min(3, trimmed.Length)] + "-••••••••••••••••" + trimmed[^4..];
    }
}

public static class PathSafety
{
    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path)) return false;
        var normalized = path.Replace('\\', '/');
        return !normalized.Split('/').Any(part => part is ".." or "" || part.Contains('\0'));
    }
}
