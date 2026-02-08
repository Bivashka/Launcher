namespace BivLauncher.Api.Options;

public sealed class LauncherUpdateOptions
{
    public const string SectionName = "LauncherUpdate";

    public string LatestVersion { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string FilePath { get; set; } = "launcher-update.json";
}
