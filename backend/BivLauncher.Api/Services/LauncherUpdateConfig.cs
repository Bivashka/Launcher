namespace BivLauncher.Api.Services;

public sealed record LauncherUpdateConfig(
    string LatestVersion,
    string DownloadUrl,
    string ReleaseNotes);
