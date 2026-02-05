using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace BivLauncher.Api.Contracts.Public;

public sealed class PublicCrashReportCreateRequest
{
    [MaxLength(64)]
    public string ProfileSlug { get; set; } = string.Empty;

    [MaxLength(128)]
    public string ServerName { get; set; } = string.Empty;

    [MaxLength(16)]
    public string RouteCode { get; set; } = string.Empty;

    [MaxLength(64)]
    public string LauncherVersion { get; set; } = string.Empty;

    [MaxLength(128)]
    public string OsVersion { get; set; } = string.Empty;

    [MaxLength(128)]
    public string JavaVersion { get; set; } = string.Empty;

    public int? ExitCode { get; set; }

    [MaxLength(512)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(128)]
    public string ErrorType { get; set; } = string.Empty;

    [MaxLength(32000)]
    public string LogExcerpt { get; set; } = string.Empty;

    public DateTime? OccurredAtUtc { get; set; }

    public JsonElement? Metadata { get; set; }
}
