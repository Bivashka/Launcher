using BivLauncher.Client.Models;

namespace BivLauncher.Client.Services;

public interface ISettingsService
{
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);
    string GetSettingsFilePath();
    string GetLogsDirectory();
    string GetLogsFilePath();
    string GetUpdatesDirectory();
    string GetDefaultInstallDirectory();
}
