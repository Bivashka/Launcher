namespace BivLauncher.Api.Contracts.Public;

public sealed record PublicCrashReportCreateResponse(
    string CrashId,
    DateTime CreatedAtUtc);
