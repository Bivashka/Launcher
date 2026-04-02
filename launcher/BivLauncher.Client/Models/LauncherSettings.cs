namespace BivLauncher.Client.Models;

public sealed class LauncherSettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string PreferredApiRegion { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public bool DebugMode { get; set; }
    public int RamMb { get; set; } = 2048;
    public string JavaMode { get; set; } = "Auto";
    public string Language { get; set; } = "ru";
    public List<string> KnownApiBaseUrls { get; set; } = [];
    public List<ProfileRouteSelection> ProfileRouteSelections { get; set; } = [];
    public string SelectedServerId { get; set; } = string.Empty;
    public string LastPlayerUsername { get; set; } = string.Empty;
    public string PlayerAuthToken { get; set; } = string.Empty;
    public string PlayerAuthTokenType { get; set; } = "Bearer";
    public string PlayerAuthUsername { get; set; } = string.Empty;
    public string PlayerAuthExternalId { get; set; } = string.Empty;
    public List<string> PlayerAuthRoles { get; set; } = [];
    public string PlayerAuthApiBaseUrl { get; set; } = string.Empty;
    public List<StoredPlayerAccount> PlayerAccounts { get; set; } = [];
    public string ActivePlayerAccountUsername { get; set; } = string.Empty;
    public string LastAutoUpdateVersionAttempted { get; set; } = string.Empty;
}

public sealed class StoredPlayerAccount
{
    public string Username { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string AuthTokenType { get; set; } = "Bearer";
    public string ExternalId { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public string ApiBaseUrl { get; set; } = string.Empty;
    public DateTime LastUsedAtUtc { get; set; }
}

public sealed class ProfileRouteSelection
{
    public string ProfileSlug { get; set; } = string.Empty;
    public string Route { get; set; } = "main";
}
