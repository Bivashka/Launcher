using BivLauncher.Client.Models;
using System.Text.Json;

namespace BivLauncher.Client.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly string ApplicationDirectory = ResolveApplicationDirectory();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = GetSettingsFilePath();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        TryMigrateLegacySettings(path);

        if (!File.Exists(path))
        {
            var defaults = CreateDefaultSettings();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        await using var stream = File.OpenRead(path);
        var loaded = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions, cancellationToken);
        return loaded ?? CreateDefaultSettings();
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        var path = GetSettingsFilePath();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    public string GetSettingsFilePath()
    {
        return Path.Combine(GetApplicationDirectory(), "settings.json");
    }

    public string GetLogsDirectory()
    {
        return Path.Combine(GetApplicationDirectory(), "logs");
    }

    public string GetLogsFilePath()
    {
        return Path.Combine(GetLogsDirectory(), "launcher.log");
    }

    public string GetUpdatesDirectory()
    {
        return Path.Combine(GetApplicationDirectory(), "updates");
    }

    public string GetDefaultInstallDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "BivLauncher", "instances");
    }

    private LauncherSettings CreateDefaultSettings()
    {
        return new LauncherSettings
        {
            InstallDirectory = GetDefaultInstallDirectory(),
            RamMb = 2048,
            JavaMode = "Auto",
            Language = "ru"
        };
    }

    private static string GetApplicationDirectory()
    {
        return ApplicationDirectory;
    }

    private static string ResolveApplicationDirectory()
    {
        var portableDirectory = Path.Combine(AppContext.BaseDirectory, "launcher-data");
        if (TryEnsureDirectory(portableDirectory))
        {
            return portableDirectory;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var fallbackDirectory = Path.Combine(appData, "BivLauncher");
        Directory.CreateDirectory(fallbackDirectory);
        return fallbackDirectory;
    }

    private static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryMigrateLegacySettings(string targetSettingsPath)
    {
        if (File.Exists(targetSettingsPath))
        {
            return;
        }

        var legacyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BivLauncher");
        var legacySettingsPath = Path.Combine(legacyDirectory, "settings.json");
        if (!File.Exists(legacySettingsPath))
        {
            return;
        }

        try
        {
            File.Copy(legacySettingsPath, targetSettingsPath, overwrite: false);
        }
        catch
        {
        }
    }
}
