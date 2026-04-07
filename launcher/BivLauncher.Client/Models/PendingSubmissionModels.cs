namespace BivLauncher.Client.Models;

public static class PendingSubmissionTypes
{
    public const string CrashReport = "crash-report";
    public const string InstallTelemetry = "install-telemetry";
    public const string SecurityViolation = "security-violation";
}

public sealed class PendingSubmissionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string AuthTokenType { get; set; } = "Bearer";
    public PublicCrashReportCreateRequest? CrashReport { get; set; }
    public PublicInstallTelemetryTrackRequest? InstallTelemetry { get; set; }
    public PublicSecurityViolationReportRequest? SecurityViolation { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
}

public sealed class PendingSubmissionStore
{
    public List<PendingSubmissionItem> Items { get; set; } = [];
}

public readonly record struct PendingSubmissionFlushResult(
    int SentCount,
    int FailedCount,
    int DroppedCount,
    int RemainingCount);
