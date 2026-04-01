namespace BivLauncher.Api.Contracts.Admin;

public sealed class SecuritySettingsUpsertRequest
{
    public int MaxConcurrentGameAccountsPerDevice { get; set; }
    public List<string> LauncherAdminUsernames { get; set; } = [];
}
