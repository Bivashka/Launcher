namespace BivLauncher.Api.Contracts.Admin;

public sealed record SecuritySettingsDto(
    int MaxConcurrentGameAccountsPerDevice,
    IReadOnlyList<string> LauncherAdminUsernames,
    string SiteCosmeticsUploadSecret,
    int GameSessionHeartbeatIntervalSeconds,
    int GameSessionExpirationSeconds,
    DateTime? UpdatedAtUtc);
