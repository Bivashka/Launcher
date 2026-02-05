namespace BivLauncher.Api.Contracts.Admin;

public sealed record CrashReportDto(
    Guid Id,
    string CrashId,
    string Status,
    string ProfileSlug,
    string ServerName,
    string RouteCode,
    string LauncherVersion,
    string OsVersion,
    string JavaVersion,
    int? ExitCode,
    string Reason,
    string ErrorType,
    string LogExcerpt,
    string MetadataJson,
    DateTime OccurredAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ResolvedAtUtc);
