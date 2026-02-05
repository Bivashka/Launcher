using System.Text.Json;

namespace BivLauncher.Client.Models;

public sealed class PublicCrashReportCreateRequest
{
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
    public DateTime? OccurredAtUtc { get; set; }
    public JsonElement? Metadata { get; set; }
}

public sealed class PublicCrashReportCreateResponse
{
    public string CrashId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
