namespace BivLauncher.Api.Contracts.Admin;

public sealed record TwoFactorAccountDto(
    Guid Id,
    string Username,
    string ExternalId,
    bool TwoFactorRequired,
    bool HasSecret,
    DateTime? TwoFactorEnrolledAtUtc,
    DateTime UpdatedAtUtc);
