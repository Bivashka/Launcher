using BivLauncher.Client.Models;

namespace BivLauncher.Client.Services;

public interface IManifestInstallerService
{
    Task<InstallResult> VerifyAndInstallAsync(
        string apiBaseUrl,
        LauncherManifest manifest,
        string installDirectory,
        IProgress<InstallProgressInfo> progress,
        CancellationToken cancellationToken = default);
}
