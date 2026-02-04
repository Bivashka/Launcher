namespace BivLauncher.Client.Models;

public sealed class LauncherSettings
{
    public string ApiBaseUrl { get; set; } = "http://localhost:8080";
    public string InstallDirectory { get; set; } = string.Empty;
    public bool DebugMode { get; set; }
    public int RamMb { get; set; } = 2048;
    public string JavaMode { get; set; } = "Auto";
    public string Language { get; set; } = "ru";
    public List<ProfileRouteSelection> ProfileRouteSelections { get; set; } = [];
    public string SelectedServerId { get; set; } = string.Empty;
    public string LastPlayerUsername { get; set; } = string.Empty;
}

public sealed class ProfileRouteSelection
{
    public string ProfileSlug { get; set; } = string.Empty;
    public string Route { get; set; } = "main";
}
