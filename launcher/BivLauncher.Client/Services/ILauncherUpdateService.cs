namespace BivLauncher.Client.Services;

public sealed record LauncherUpdateDownloadProgress(long DownloadedBytes, long? TotalBytes);

public interface ILauncherUpdateService
{
    Task<string> DownloadPackageAsync(
        string downloadUrl,
        string latestVersion,
        IProgress<LauncherUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    void ScheduleInstallAndRestart(string packagePath, string executablePath);
}
