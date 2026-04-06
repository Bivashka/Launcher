using BivLauncher.Client.Models;

namespace BivLauncher.Client.Services;

public interface IGameLaunchService
{
    Task<LaunchResult> LaunchAsync(
        LauncherManifest manifest,
        LauncherSettings settings,
        GameLaunchRoute route,
        string instanceDirectory,
        Action<string> onProcessLine,
        Action<int>? onProcessStarted = null,
        CancellationToken cancellationToken = default);
}
