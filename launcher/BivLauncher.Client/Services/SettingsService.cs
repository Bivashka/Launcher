using BivLauncher.Client.Models;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Client.Services;

public sealed class SettingsService : ISettingsService
{
    private const string DefaultProjectDirectoryName = "BivLauncher";
    private static readonly string ApplicationDirectory = ResolveApplicationDirectory();
    private readonly object _syncRoot = new();
    private string _projectDirectoryName = DefaultProjectDirectoryName;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public void ConfigureProjectDirectoryName(string? projectDirectoryName)
    {
        var normalized = NormalizeProjectDirectoryName(projectDirectoryName);
        lock (_syncRoot)
        {
            _projectDirectoryName = normalized;
        }
    }

    public string GetProjectDirectoryName()
    {
        lock (_syncRoot)
        {
            return _projectDirectoryName;
        }
    }

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
        return Path.Combine(home, GetProjectDirectoryName(), "instances");
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
        if (TryEnsureWritableDirectory(portableDirectory))
        {
            return portableDirectory;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var fallbackDirectory = Path.Combine(appData, DefaultProjectDirectoryName);
        if (TryEnsureWritableDirectory(fallbackDirectory))
        {
            return fallbackDirectory;
        }

        var tempFallbackDirectory = Path.Combine(Path.GetTempPath(), DefaultProjectDirectoryName);
        Directory.CreateDirectory(tempFallbackDirectory);
        return tempFallbackDirectory;
    }

    private static bool TryEnsureWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
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
            DefaultProjectDirectoryName);
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

    private static string NormalizeProjectDirectoryName(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return DefaultProjectDirectoryName;
        }

        var builder = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' ? ch : '_');
        }

        var sanitized = builder.ToString().Trim().Trim('.');
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64].Trim();
        }

        return string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or ".."
            ? DefaultProjectDirectoryName
            : sanitized;
    }
}
