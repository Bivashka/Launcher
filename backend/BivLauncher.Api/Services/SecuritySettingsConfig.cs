namespace BivLauncher.Api.Services;

public sealed record SecuritySettingsConfig(
    int MaxConcurrentGameAccountsPerDevice,
    IReadOnlyList<string> LauncherAdminUsernames,
    string SiteCosmeticsUploadSecret,
    int GameSessionHeartbeatIntervalSeconds,
    int GameSessionExpirationSeconds,
    DateTime? UpdatedAtUtc);
