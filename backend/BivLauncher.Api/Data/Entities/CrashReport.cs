namespace BivLauncher.Api.Data.Entities;

public sealed class CrashReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CrashId { get; set; } = string.Empty;
    public string Status { get; set; } = "new";
    public string ProfileSlug { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string RouteCode { get; set; } = string.Empty;
    public string LauncherVersion { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string JavaVersion { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string LogExcerpt { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}
