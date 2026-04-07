namespace BivLauncher.Api.Data.Entities;

public sealed class SecuritySettingsState
{
    public int Id { get; set; }
    public int MaxConcurrentGameAccountsPerDevice { get; set; }
    public string LauncherAdminUsernamesJson { get; set; } = "[]";
    public string SiteCosmeticsUploadSecret { get; set; } = string.Empty;
    public int GameSessionHeartbeatIntervalSeconds { get; set; }
    public int GameSessionExpirationSeconds { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
