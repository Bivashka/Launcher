using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class DeliverySettingsProvider(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IOptions<DeliverySettingsOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<DeliverySettingsProvider> logger) : IDeliverySettingsProvider
{
    private const int SettingsRowId = 1;

    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private DeliverySettingsConfig? _cached;

    public DeliverySettingsConfig GetCachedSettings()
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

    public Task<DeliverySettingsConfig> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return LoadAndCacheAsync(cancellationToken);
    }

    public async Task<DeliverySettingsConfig> SaveSettingsAsync(
        DeliverySettingsConfig settings,
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

    private async Task<DeliverySettingsConfig> LoadAndCacheAsync(CancellationToken cancellationToken)
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

    private async Task<DeliverySettingsConfig> LoadSettingsAsync(CancellationToken cancellationToken)
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

    private async Task<DeliverySettingsConfig?> TryLoadFromDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await dbContext.DeliverySettingsStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == SettingsRowId, cancellationToken);
            return state is null ? null : Map(state);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load delivery settings from database. Falling back to disk.");
            return null;
        }
    }

    private async Task SaveToDatabaseAsync(DeliverySettingsConfig settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await dbContext.DeliverySettingsStates
            .FirstOrDefaultAsync(x => x.Id == SettingsRowId, cancellationToken);
        if (state is null)
        {
            state = new DeliverySettingsState
            {
                Id = SettingsRowId
            };
            dbContext.DeliverySettingsStates.Add(state);
        }

        state.PublicBaseUrl = settings.PublicBaseUrl;
        state.AssetBaseUrl = settings.AssetBaseUrl;
        state.FallbackApiBaseUrlsJson = JsonSerializer.Serialize(settings.FallbackApiBaseUrls ?? [], WriteJsonOptions);
        state.LauncherApiBaseUrlRu = settings.LauncherApiBaseUrlRu;
        state.LauncherApiBaseUrlEu = settings.LauncherApiBaseUrlEu;
        state.PublicBaseUrlRu = settings.PublicBaseUrlRu;
        state.PublicBaseUrlEu = settings.PublicBaseUrlEu;
        state.AssetBaseUrlRu = settings.AssetBaseUrlRu;
        state.AssetBaseUrlEu = settings.AssetBaseUrlEu;
        state.FallbackApiBaseUrlsRuJson = JsonSerializer.Serialize(settings.FallbackApiBaseUrlsRu ?? [], WriteJsonOptions);
        state.FallbackApiBaseUrlsEuJson = JsonSerializer.Serialize(settings.FallbackApiBaseUrlsEu ?? [], WriteJsonOptions);
        state.UpdatedAtUtc = settings.UpdatedAtUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task TrySaveToDatabaseAsync(DeliverySettingsConfig settings, CancellationToken cancellationToken)
    {
        try
        {
            await SaveToDatabaseAsync(settings, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist delivery settings into database during fallback migration.");
        }
    }

    private DeliverySettingsConfig LoadFromDiskOrDefault()
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
            var loaded = JsonSerializer.Deserialize<DeliverySettingsConfig>(payload, ReadJsonOptions);
            return NormalizeSettings(loaded ?? BuildDefaultSettings());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load delivery settings from {Path}. Falling back to defaults.", settingsPath);
            return BuildDefaultSettings();
        }
    }

    private async Task TrySaveToDiskBackupAsync(DeliverySettingsConfig settings, CancellationToken cancellationToken)
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
            logger.LogWarning(ex, "Could not write delivery settings backup to disk.");
        }
    }

    private string GetSettingsPath()
    {
        var configuredPath = (options.Value.FilePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "delivery.json";
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

        var legacyPath = Path.Combine(environment.ContentRootPath, "delivery.json");
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
            logger.LogWarning(ex, "Delivery settings migration from {LegacyPath} to {SettingsPath} failed.", legacyPath, settingsPath);
        }
    }

    private DeliverySettingsConfig BuildDefaultSettings()
    {
        var configuredPublicBaseUrl = NormalizeAbsoluteUrl(
            configuration["PUBLIC_BASE_URL"] ?? configuration["PublicBaseUrl"]);
        if (string.IsNullOrWhiteSpace(configuredPublicBaseUrl))
        {
            configuredPublicBaseUrl = "http://195.43.142.97";
        }

        var defaultFallbackApiBaseUrls = new List<string> { "http://95.217.99.17:8080" };
        var defaultEuLauncherApiBaseUrl = defaultFallbackApiBaseUrls
            .Select(NormalizeAbsoluteUrl)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;

        return new DeliverySettingsConfig(
            PublicBaseUrl: configuredPublicBaseUrl,
            AssetBaseUrl: configuredPublicBaseUrl,
            FallbackApiBaseUrls: defaultFallbackApiBaseUrls,
            UpdatedAtUtc: null,
            LauncherApiBaseUrlRu: configuredPublicBaseUrl,
            LauncherApiBaseUrlEu: defaultEuLauncherApiBaseUrl,
            PublicBaseUrlRu: configuredPublicBaseUrl,
            PublicBaseUrlEu: defaultEuLauncherApiBaseUrl,
            AssetBaseUrlRu: configuredPublicBaseUrl,
            AssetBaseUrlEu: defaultEuLauncherApiBaseUrl,
            FallbackApiBaseUrlsRu: defaultFallbackApiBaseUrls,
            FallbackApiBaseUrlsEu: []);
    }

    private static DeliverySettingsConfig Map(DeliverySettingsState state)
    {
        IReadOnlyList<string> fallbackApiBaseUrls = [];
        IReadOnlyList<string> fallbackApiBaseUrlsRu = [];
        IReadOnlyList<string> fallbackApiBaseUrlsEu = [];

        try
        {
            fallbackApiBaseUrls = JsonSerializer.Deserialize<List<string>>(state.FallbackApiBaseUrlsJson ?? "[]", ReadJsonOptions)
                ?? [];
        }
        catch
        {
            fallbackApiBaseUrls = [];
        }

        try
        {
            fallbackApiBaseUrlsRu = JsonSerializer.Deserialize<List<string>>(state.FallbackApiBaseUrlsRuJson ?? "[]", ReadJsonOptions)
                ?? [];
        }
        catch
        {
            fallbackApiBaseUrlsRu = [];
        }

        try
        {
            fallbackApiBaseUrlsEu = JsonSerializer.Deserialize<List<string>>(state.FallbackApiBaseUrlsEuJson ?? "[]", ReadJsonOptions)
                ?? [];
        }
        catch
        {
            fallbackApiBaseUrlsEu = [];
        }

        return NormalizeSettings(new DeliverySettingsConfig(
            PublicBaseUrl: state.PublicBaseUrl,
            AssetBaseUrl: state.AssetBaseUrl,
            FallbackApiBaseUrls: fallbackApiBaseUrls,
            UpdatedAtUtc: state.UpdatedAtUtc,
            LauncherApiBaseUrlRu: state.LauncherApiBaseUrlRu,
            LauncherApiBaseUrlEu: state.LauncherApiBaseUrlEu,
            PublicBaseUrlRu: state.PublicBaseUrlRu,
            PublicBaseUrlEu: state.PublicBaseUrlEu,
            AssetBaseUrlRu: state.AssetBaseUrlRu,
            AssetBaseUrlEu: state.AssetBaseUrlEu,
            FallbackApiBaseUrlsRu: fallbackApiBaseUrlsRu,
            FallbackApiBaseUrlsEu: fallbackApiBaseUrlsEu));
    }

    private static DeliverySettingsConfig NormalizeSettings(DeliverySettingsConfig settings)
    {
        var normalizedPublicBaseUrl = NormalizeAbsoluteUrl(settings.PublicBaseUrl);
        var normalizedAssetBaseUrl = NormalizeAbsoluteUrl(settings.AssetBaseUrl);
        var normalizedLauncherApiBaseUrlRu = NormalizeAbsoluteUrl(settings.LauncherApiBaseUrlRu);
        var normalizedLauncherApiBaseUrlEu = NormalizeAbsoluteUrl(settings.LauncherApiBaseUrlEu);
        var normalizedFallbacks = NormalizeFallbacks(
            settings.FallbackApiBaseUrls,
            normalizedPublicBaseUrl,
            normalizedAssetBaseUrl);

        var normalizedPublicBaseUrlRu = NormalizeAbsoluteUrl(settings.PublicBaseUrlRu);
        var normalizedPublicBaseUrlEu = NormalizeAbsoluteUrl(settings.PublicBaseUrlEu);
        var normalizedAssetBaseUrlRu = NormalizeAbsoluteUrl(settings.AssetBaseUrlRu);
        var normalizedAssetBaseUrlEu = NormalizeAbsoluteUrl(settings.AssetBaseUrlEu);

        if (string.IsNullOrWhiteSpace(normalizedPublicBaseUrlRu))
        {
            normalizedPublicBaseUrlRu = normalizedPublicBaseUrl;
        }

        if (string.IsNullOrWhiteSpace(normalizedPublicBaseUrlEu))
        {
            normalizedPublicBaseUrlEu = !string.IsNullOrWhiteSpace(normalizedLauncherApiBaseUrlEu)
                ? normalizedLauncherApiBaseUrlEu
                : normalizedPublicBaseUrl;
        }

        if (string.IsNullOrWhiteSpace(normalizedAssetBaseUrlRu))
        {
            normalizedAssetBaseUrlRu = !string.IsNullOrWhiteSpace(normalizedAssetBaseUrl)
                ? normalizedAssetBaseUrl
                : normalizedPublicBaseUrlRu;
        }

        if (string.IsNullOrWhiteSpace(normalizedAssetBaseUrlEu))
        {
            normalizedAssetBaseUrlEu = !string.IsNullOrWhiteSpace(normalizedAssetBaseUrl)
                ? normalizedAssetBaseUrl
                : normalizedPublicBaseUrlEu;
        }

        var normalizedFallbacksRu = NormalizeFallbacks(
            settings.FallbackApiBaseUrlsRu is { Count: > 0 } ? settings.FallbackApiBaseUrlsRu : settings.FallbackApiBaseUrls,
            normalizedPublicBaseUrlRu,
            normalizedAssetBaseUrlRu);
        var normalizedFallbacksEu = NormalizeFallbacks(
            settings.FallbackApiBaseUrlsEu,
            normalizedPublicBaseUrlEu,
            normalizedAssetBaseUrlEu);

        return new DeliverySettingsConfig(
            PublicBaseUrl: normalizedPublicBaseUrl,
            AssetBaseUrl: normalizedAssetBaseUrl,
            FallbackApiBaseUrls: normalizedFallbacks,
            UpdatedAtUtc: DateTime.UtcNow,
            LauncherApiBaseUrlRu: normalizedLauncherApiBaseUrlRu,
            LauncherApiBaseUrlEu: normalizedLauncherApiBaseUrlEu,
            PublicBaseUrlRu: normalizedPublicBaseUrlRu,
            PublicBaseUrlEu: normalizedPublicBaseUrlEu,
            AssetBaseUrlRu: normalizedAssetBaseUrlRu,
            AssetBaseUrlEu: normalizedAssetBaseUrlEu,
            FallbackApiBaseUrlsRu: normalizedFallbacksRu,
            FallbackApiBaseUrlsEu: normalizedFallbacksEu);
    }

    private static List<string> NormalizeFallbacks(
        IEnumerable<string>? rawValues,
        string normalizedPublicBaseUrl,
        string normalizedAssetBaseUrl)
    {
        return (rawValues ?? [])
            .Select(NormalizeAbsoluteUrl)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(value =>
                !string.Equals(value, normalizedPublicBaseUrl, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, normalizedAssetBaseUrl, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string NormalizeAbsoluteUrl(string? rawValue)
    {
        var trimmed = (rawValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        return uri.ToString().TrimEnd('/');
    }
}
