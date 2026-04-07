using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class SecuritySettingsProvider(
    IWebHostEnvironment environment,
    IOptions<SecuritySettingsOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<SecuritySettingsProvider> logger) : ISecuritySettingsProvider
{
    private const int SettingsRowId = 1;
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
            if (_cached is not null)
            {
                return _cached;
            }
        }

        return LoadAndCacheAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task<SecuritySettingsConfig> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return LoadAndCacheAsync(cancellationToken);
    }

    public async Task<SecuritySettingsConfig> SaveSettingsAsync(
        SecuritySettingsConfig settings,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSettings(settings);
        await SaveToDatabaseAsync(normalized, cancellationToken);
        await TrySaveToDiskBackupAsync(normalized, cancellationToken);

        lock (_sync)
        {
            _cached = normalized;
        }

        return normalized;
    }

    private async Task<SecuritySettingsConfig> LoadAndCacheAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_cached is not null)
            {
                return _cached;
            }
        }

        var loaded = await LoadSettingsAsync(cancellationToken);
        lock (_sync)
        {
            _cached ??= loaded;
            return _cached;
        }
    }

    private async Task<SecuritySettingsConfig> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var persisted = await TryLoadFromDatabaseAsync(cancellationToken);
        if (persisted is not null)
        {
            await TrySaveToDiskBackupAsync(persisted, cancellationToken);
            return persisted;
        }

        var fileSettings = LoadFromDiskOrDefault();
        await TrySaveToDatabaseAsync(fileSettings, cancellationToken);
        return fileSettings;
    }

    private async Task<SecuritySettingsConfig?> TryLoadFromDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await dbContext.SecuritySettingsStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == SettingsRowId, cancellationToken);
            return state is null ? null : Map(state);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load security settings from database. Falling back to disk.");
            return null;
        }
    }

    private async Task SaveToDatabaseAsync(SecuritySettingsConfig settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await dbContext.SecuritySettingsStates
            .FirstOrDefaultAsync(x => x.Id == SettingsRowId, cancellationToken);
        if (state is null)
        {
            state = new SecuritySettingsState
            {
                Id = SettingsRowId
            };
            dbContext.SecuritySettingsStates.Add(state);
        }

        state.MaxConcurrentGameAccountsPerDevice = settings.MaxConcurrentGameAccountsPerDevice;
        state.LauncherAdminUsernamesJson = JsonSerializer.Serialize(settings.LauncherAdminUsernames ?? [], WriteJsonOptions);
        state.SiteCosmeticsUploadSecret = settings.SiteCosmeticsUploadSecret;
        state.GameSessionHeartbeatIntervalSeconds = settings.GameSessionHeartbeatIntervalSeconds;
        state.GameSessionExpirationSeconds = settings.GameSessionExpirationSeconds;
        state.UpdatedAtUtc = settings.UpdatedAtUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task TrySaveToDatabaseAsync(SecuritySettingsConfig settings, CancellationToken cancellationToken)
    {
        try
        {
            await SaveToDatabaseAsync(settings, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist security settings into database during fallback migration.");
        }
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

    private async Task TrySaveToDiskBackupAsync(SecuritySettingsConfig settings, CancellationToken cancellationToken)
    {
        try
        {
            var settingsPath = GetSettingsPath();
            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(settingsPath);
            await JsonSerializer.SerializeAsync(stream, settings, WriteJsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write security settings backup to disk.");
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

    private static SecuritySettingsConfig Map(SecuritySettingsState state)
    {
        IReadOnlyList<string> launcherAdminUsernames = [];
        try
        {
            launcherAdminUsernames = JsonSerializer.Deserialize<List<string>>(state.LauncherAdminUsernamesJson ?? "[]", ReadJsonOptions)
                ?? [];
        }
        catch
        {
            launcherAdminUsernames = [];
        }

        return NormalizeSettings(new SecuritySettingsConfig(
            state.MaxConcurrentGameAccountsPerDevice,
            launcherAdminUsernames,
            state.SiteCosmeticsUploadSecret,
            state.GameSessionHeartbeatIntervalSeconds,
            state.GameSessionExpirationSeconds,
            state.UpdatedAtUtc));
    }

    private static SecuritySettingsConfig BuildDefaultSettings()
    {
        return new SecuritySettingsConfig(
            MaxConcurrentGameAccountsPerDevice: 0,
            LauncherAdminUsernames: [],
            SiteCosmeticsUploadSecret: string.Empty,
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
        var siteCosmeticsUploadSecret = (settings.SiteCosmeticsUploadSecret ?? string.Empty).Trim();
        if (siteCosmeticsUploadSecret.Length > 256)
        {
            siteCosmeticsUploadSecret = siteCosmeticsUploadSecret[..256];
        }

        return new SecuritySettingsConfig(
            MaxConcurrentGameAccountsPerDevice: Math.Clamp(settings.MaxConcurrentGameAccountsPerDevice, 0, 16),
            LauncherAdminUsernames: launcherAdminUsernames,
            SiteCosmeticsUploadSecret: siteCosmeticsUploadSecret,
            GameSessionHeartbeatIntervalSeconds: heartbeatSeconds,
            GameSessionExpirationSeconds: expirationSeconds,
            UpdatedAtUtc: DateTime.UtcNow);
    }
}
