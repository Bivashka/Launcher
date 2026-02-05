namespace BivLauncher.Api.Contracts.Admin;

public sealed record DiscordRpcSettingsDto(
    bool Enabled,
    bool PrivacyMode,
    DateTime? UpdatedAtUtc);
