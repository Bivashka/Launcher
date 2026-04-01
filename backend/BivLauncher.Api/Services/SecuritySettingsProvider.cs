using BivLauncher.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class SecuritySettingsProvider(
    IWebHostEnvironment environment,
    IOptions<SecuritySettingsOptions> options,
    ILogger<SecuritySettingsProvider> logger) : ISecuritySettingsProvider
{
    private const int DefaultHeartbeatIntervalSeconds = 45;
    private const int DefaultExpirationSeconds = 150;

    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private SecuritySettingsConfig? _cached;

    public SecuritySettingsConfig GetCachedSettings()
    {
        lock (_sync)
        {
            _cached ??= LoadFromDiskOrDefault();
            return _cached;
        }
    }

    public Task<SecuritySettingsConfig> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetCachedSettings());
    }

    public async Task<SecuritySettingsConfig> SaveSettingsAsync(
        SecuritySettingsConfig settings,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSettings(settings);
        var settingsPath = GetSettingsPath();
        var directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var stream = File.Create(settingsPath))
        {
            await JsonSerializer.SerializeAsync(stream, normalized, WriteJsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        lock (_sync)
        {
            _cached = normalized;
        }

        return normalized;
    }

    private SecuritySettingsConfig LoadFromDiskOrDefault()
    {
        var settingsPath = GetSettingsPath();
        TryMigrateLegacyFile(settingsPath);

        if (!File.Exists(settingsPath))
        {
            return BuildDefaultSettings();
        }

        try
        {
            var payload = File.ReadAllText(settingsPath);
            var loaded = JsonSerializer.Deserialize<SecuritySettingsConfig>(payload, ReadJsonOptions);
            return NormalizeSettings(loaded ?? BuildDefaultSettings());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load security settings from {Path}. Falling back to defaults.", settingsPath);
            return BuildDefaultSettings();
        }
    }

    private string GetSettingsPath()
    {
        var configuredPath = (options.Value.FilePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "security-settings.json";
        }

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
    }

    private void TryMigrateLegacyFile(string settingsPath)
    {
        if (File.Exists(settingsPath))
        {
            return;
        }

        var legacyPath = Path.Combine(environment.ContentRootPath, "security-settings.json");
        if (string.Equals(
                Path.GetFullPath(legacyPath),
                Path.GetFullPath(settingsPath),
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(legacyPath, settingsPath, overwrite: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Security settings migration from {LegacyPath} to {SettingsPath} failed.", legacyPath, settingsPath);
        }
    }

    private static SecuritySettingsConfig BuildDefaultSettings()
    {
        return new SecuritySettingsConfig(
            MaxConcurrentGameAccountsPerDevice: 0,
            LauncherAdminUsernames: [],
            GameSessionHeartbeatIntervalSeconds: DefaultHeartbeatIntervalSeconds,
            GameSessionExpirationSeconds: DefaultExpirationSeconds,
            UpdatedAtUtc: null);
    }

    private static SecuritySettingsConfig NormalizeSettings(SecuritySettingsConfig settings)
    {
        var heartbeatSeconds = Math.Clamp(settings.GameSessionHeartbeatIntervalSeconds, 15, 300);
        var expirationSeconds = Math.Clamp(settings.GameSessionExpirationSeconds, heartbeatSeconds + 30, 900);
        var launcherAdminUsernames = (settings.LauncherAdminUsernames ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToArray();

        return new SecuritySettingsConfig(
            MaxConcurrentGameAccountsPerDevice: Math.Clamp(settings.MaxConcurrentGameAccountsPerDevice, 0, 16),
            LauncherAdminUsernames: launcherAdminUsernames,
            GameSessionHeartbeatIntervalSeconds: heartbeatSeconds,
            GameSessionExpirationSeconds: expirationSeconds,
            UpdatedAtUtc: DateTime.UtcNow);
    }
}
