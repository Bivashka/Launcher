namespace BivLauncher.Api.Contracts.Admin;

public sealed record BanDto(
    Guid Id,
    Guid? AccountId,
    string AccountUsername,
    string AccountExternalId,
    string HwidHash,
    string Reason,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    bool Active);
