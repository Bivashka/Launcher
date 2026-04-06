namespace BivLauncher.Api.Contracts.Public;

public sealed record PublicSecurityViolationReportResponse(
    bool Banned,
    bool Exempt,
    DateTime? ExpiresAtUtc,
    string Reason);
