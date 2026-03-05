using BivLauncher.Client.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Client.Services;

public sealed class SettingsService : ISettingsService
{
    private const string DefaultProjectDirectoryName = "BivLauncher";
    private const string PortableModeEnvVar = "BIVLAUNCHER_PORTABLE_MODE";
    private const string PortableModeMarkerFileName = "portable-mode.flag";
    private const string ProtectedTokenPrefix = "enc:v1:dpapi:";
    private static readonly byte[] TokenProtectionEntropy = Encoding.UTF8.GetBytes("BivLauncher.TokenProtection.v1");
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
        return loaded is null
            ? CreateDefaultSettings()
            : NormalizeLoadedSettings(loaded);
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
        snapshot.PlayerAuthToken = ProtectToken(snapshot.PlayerAuthToken);
        snapshot.PlayerAccounts = snapshot.PlayerAccounts
            .Select(account =>
            {
                var cloned = CloneStoredPlayerAccount(account);
                cloned.AuthToken = ProtectToken(cloned.AuthToken);
                return cloned;
            })
            .ToList();
        return snapshot;
    }

    private static LauncherSettings NormalizeLoadedSettings(LauncherSettings settings)
    {
        var normalized = CloneSettings(settings);
        normalized.PlayerAuthToken = UnprotectToken(normalized.PlayerAuthToken);
        normalized.PlayerAccounts = normalized.PlayerAccounts
            .Select(account =>
            {
                var cloned = CloneStoredPlayerAccount(account);
                cloned.AuthToken = UnprotectToken(cloned.AuthToken);
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
            InstallDirectory = source.InstallDirectory,
            DebugMode = source.DebugMode,
            RamMb = source.RamMb,
            JavaMode = source.JavaMode,
            Language = source.Language,
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

    private static string ProtectToken(string? rawToken)
    {
        var token = (rawToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        if (token.StartsWith(ProtectedTokenPrefix, StringComparison.Ordinal))
        {
            return token;
        }

        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        try
        {
            var tokenBytes = Encoding.UTF8.GetBytes(token);
            var protectedBytes = ProtectedData.Protect(tokenBytes, TokenProtectionEntropy, DataProtectionScope.CurrentUser);
            return ProtectedTokenPrefix + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string UnprotectToken(string? persistedToken)
    {
        var value = (persistedToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!value.StartsWith(ProtectedTokenPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        var payload = value[ProtectedTokenPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(payload);
            var tokenBytes = ProtectedData.Unprotect(protectedBytes, TokenProtectionEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(tokenBytes).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
