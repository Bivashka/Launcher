namespace BivLauncher.Api.Services;

public sealed record SecuritySettingsConfig(
    int MaxConcurrentGameAccountsPerDevice,
    int GameSessionHeartbeatIntervalSeconds,
    int GameSessionExpirationSeconds,
    DateTime? UpdatedAtUtc);
