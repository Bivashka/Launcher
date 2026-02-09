using BivLauncher.Client.Models;

namespace BivLauncher.Client.Services;

public interface ISettingsService
{
    void ConfigureProjectDirectoryName(string? projectDirectoryName);
    string GetProjectDirectoryName();
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);
    string GetSettingsFilePath();
    string GetLogsDirectory();
    string GetLogsFilePath();
    string GetUpdatesDirectory();
    string GetDefaultInstallDirectory();
}
