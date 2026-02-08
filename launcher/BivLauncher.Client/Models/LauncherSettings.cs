namespace BivLauncher.Client.Models;

public sealed class LauncherSettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public bool DebugMode { get; set; }
    public int RamMb { get; set; } = 2048;
    public string JavaMode { get; set; } = "Auto";
    public string Language { get; set; } = "ru";
    public List<ProfileRouteSelection> ProfileRouteSelections { get; set; } = [];
    public string SelectedServerId { get; set; } = string.Empty;
    public string LastPlayerUsername { get; set; } = string.Empty;
    public string PlayerAuthToken { get; set; } = string.Empty;
    public string PlayerAuthTokenType { get; set; } = "Bearer";
    public string PlayerAuthUsername { get; set; } = string.Empty;
    public string PlayerAuthExternalId { get; set; } = string.Empty;
    public List<string> PlayerAuthRoles { get; set; } = [];
    public string PlayerAuthApiBaseUrl { get; set; } = string.Empty;
}

public sealed class ProfileRouteSelection
{
    public string ProfileSlug { get; set; } = string.Empty;
    public string Route { get; set; } = "main";
}
