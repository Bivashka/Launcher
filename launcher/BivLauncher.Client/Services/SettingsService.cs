using BivLauncher.Client.Models;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Client.Services;

public sealed class SettingsService : ISettingsService
{
    private const string DefaultProjectDirectoryName = "BivLauncher";
    private const string PortableModeEnvVar = "BIVLAUNCHER_PORTABLE_MODE";
    private const string PortableModeMarkerFileName = "portable-mode.flag";
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

        LauncherSettings? loaded;
        await using (var stream = File.OpenRead(path))
        {
            loaded = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions, cancellationToken);
        }

        if (loaded is null)
        {
            return CreateDefaultSettings();
        }

        var normalized = NormalizeLoadedSettings(loaded);
        if (NeedsPersistRewrite(loaded))
        {
            await SaveAsync(normalized, cancellationToken);
        }

        return normalized;
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        var path = GetSettingsFilePath();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var persistSnapshot = PreparePersistSnapshot(settings);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, persistSnapshot, JsonOptions, cancellationToken);
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
            PreferredApiRegion = string.Empty,
            RamMb = 2048,
            JavaMode = "Auto",
            Language = "ru",
            KnownApiBaseUrls = []
        };
    }

    private static string GetApplicationDirectory()
    {
        return ApplicationDirectory;
    }

    private static string ResolveApplicationDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDataDirectory = Path.Combine(appData, DefaultProjectDirectoryName);
        var portableDirectory = Path.Combine(AppContext.BaseDirectory, "launcher-data");

        if (ShouldUsePortableMode() &&
            TryEnsureWritableDirectory(portableDirectory))
        {
            return portableDirectory;
        }

        if (TryEnsureWritableDirectory(appDataDirectory))
        {
            return appDataDirectory;
        }

        if (TryEnsureWritableDirectory(portableDirectory))
        {
            return portableDirectory;
        }

        var tempFallbackDirectory = Path.Combine(Path.GetTempPath(), DefaultProjectDirectoryName);
        Directory.CreateDirectory(tempFallbackDirectory);
        return tempFallbackDirectory;
    }

    private static bool ShouldUsePortableMode()
    {
        var envValue = Environment.GetEnvironmentVariable(PortableModeEnvVar);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return IsTruthy(envValue);
        }

        var markerPath = Path.Combine(AppContext.BaseDirectory, PortableModeMarkerFileName);
        return File.Exists(markerPath);
    }

    private static bool IsTruthy(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "1" => true,
            "true" => true,
            "yes" => true,
            "on" => true,
            _ => false
        };
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

    private static LauncherSettings PreparePersistSnapshot(LauncherSettings settings)
    {
        var snapshot = CloneSettings(settings);
        snapshot.ApiBaseUrl = ProtectEndpoint(snapshot.ApiBaseUrl);
        snapshot.PlayerAuthToken = ProtectToken(snapshot.PlayerAuthToken);
        snapshot.PlayerAuthApiBaseUrl = ProtectEndpoint(snapshot.PlayerAuthApiBaseUrl);
        snapshot.PlayerAccounts = snapshot.PlayerAccounts
            .Select(account =>
            {
                var cloned = CloneStoredPlayerAccount(account);
                cloned.AuthToken = ProtectToken(cloned.AuthToken);
                cloned.ApiBaseUrl = ProtectEndpoint(cloned.ApiBaseUrl);
                return cloned;
            })
            .ToList();
        return snapshot;
    }

    private static LauncherSettings NormalizeLoadedSettings(LauncherSettings settings)
    {
        var normalized = CloneSettings(settings);
        normalized.ApiBaseUrl = UnprotectEndpoint(normalized.ApiBaseUrl);
        normalized.PlayerAuthToken = UnprotectToken(normalized.PlayerAuthToken);
        normalized.PlayerAuthApiBaseUrl = UnprotectEndpoint(normalized.PlayerAuthApiBaseUrl);
        normalized.PlayerAccounts = normalized.PlayerAccounts
            .Select(account =>
            {
                var cloned = CloneStoredPlayerAccount(account);
                cloned.AuthToken = UnprotectToken(cloned.AuthToken);
                cloned.ApiBaseUrl = UnprotectEndpoint(cloned.ApiBaseUrl);
                return cloned;
            })
            .ToList();
        return normalized;
    }

    private static LauncherSettings CloneSettings(LauncherSettings source)
    {
        return new LauncherSettings
        {
            ApiBaseUrl = source.ApiBaseUrl,
            PreferredApiRegion = source.PreferredApiRegion,
            InstallDirectory = source.InstallDirectory,
            DebugMode = source.DebugMode,
            RamMb = source.RamMb,
            JavaMode = source.JavaMode,
            Language = source.Language,
            KnownApiBaseUrls = [.. (source.KnownApiBaseUrls ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            ProfileRouteSelections = (source.ProfileRouteSelections ?? [])
                .Select(selection => new ProfileRouteSelection
                {
                    ProfileSlug = selection.ProfileSlug,
                    Route = selection.Route
                })
                .ToList(),
            SelectedServerId = source.SelectedServerId,
            LastPlayerUsername = source.LastPlayerUsername,
            PlayerAuthToken = source.PlayerAuthToken,
            PlayerAuthTokenType = source.PlayerAuthTokenType,
            PlayerAuthUsername = source.PlayerAuthUsername,
            PlayerAuthExternalId = source.PlayerAuthExternalId,
            PlayerAuthRoles = [.. (source.PlayerAuthRoles ?? [])],
            PlayerAuthApiBaseUrl = source.PlayerAuthApiBaseUrl,
            PlayerAccounts = (source.PlayerAccounts ?? [])
                .Select(CloneStoredPlayerAccount)
                .ToList(),
            ActivePlayerAccountUsername = source.ActivePlayerAccountUsername,
            LastAutoUpdateVersionAttempted = source.LastAutoUpdateVersionAttempted
        };
    }

    private static StoredPlayerAccount CloneStoredPlayerAccount(StoredPlayerAccount source)
    {
        return new StoredPlayerAccount
        {
            Username = source.Username,
            AuthToken = source.AuthToken,
            AuthTokenType = source.AuthTokenType,
            ExternalId = source.ExternalId,
            Roles = [.. (source.Roles ?? [])],
            ApiBaseUrl = source.ApiBaseUrl,
            LastUsedAtUtc = source.LastUsedAtUtc
        };
    }

    private static bool NeedsPersistRewrite(LauncherSettings settings)
    {
        return HasPlainEndpoint(settings.ApiBaseUrl) ||
               HasPlainToken(settings.PlayerAuthToken) ||
               HasPlainEndpoint(settings.PlayerAuthApiBaseUrl) ||
               (settings.PlayerAccounts ?? []).Any(account =>
                   HasPlainToken(account.AuthToken) ||
                   HasPlainEndpoint(account.ApiBaseUrl));
    }

    private static bool HasPlainToken(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(normalized) && !LocalSecretProtector.IsProtectedToken(normalized);
    }

    private static bool HasPlainEndpoint(string? value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var normalized = (value ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(normalized) && !LocalSecretProtector.IsProtectedEndpoint(normalized);
    }

    private static string ProtectToken(string? rawToken)
    {
        return LocalSecretProtector.ProtectToken(rawToken);
    }

    private static string UnprotectToken(string? persistedToken)
    {
        return LocalSecretProtector.UnprotectToken(persistedToken);
    }

    private static string ProtectEndpoint(string? rawEndpoint)
    {
        return LocalSecretProtector.ProtectEndpoint(rawEndpoint);
    }

    private static string UnprotectEndpoint(string? persistedEndpoint)
    {
        return LocalSecretProtector.UnprotectEndpoint(persistedEndpoint);
    }
}
