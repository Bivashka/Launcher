namespace BivLauncher.Api.Contracts.Admin;

public sealed record TwoFactorSettingsDto(
    bool Enabled,
    DateTime? UpdatedAtUtc);
