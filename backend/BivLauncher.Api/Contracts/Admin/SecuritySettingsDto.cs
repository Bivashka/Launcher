namespace BivLauncher.Api.Contracts.Admin;

public sealed record SecuritySettingsDto(
    int MaxConcurrentGameAccountsPerDevice,
    int GameSessionHeartbeatIntervalSeconds,
    int GameSessionExpirationSeconds,
    DateTime? UpdatedAtUtc);
