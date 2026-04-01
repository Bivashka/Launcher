namespace BivLauncher.Api.Contracts.Admin;

public sealed record SecuritySettingsDto(
    int MaxConcurrentGameAccountsPerDevice,
    IReadOnlyList<string> LauncherAdminUsernames,
    int GameSessionHeartbeatIntervalSeconds,
    int GameSessionExpirationSeconds,
    DateTime? UpdatedAtUtc);
