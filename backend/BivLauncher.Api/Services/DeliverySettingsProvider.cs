using BivLauncher.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class DeliverySettingsProvider(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IOptions<DeliverySettingsOptions> options,
    ILogger<DeliverySettingsProvider> logger) : IDeliverySettingsProvider
{
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
            _cached ??= LoadFromDiskOrDefault();
            return _cached;
        }
    }

    public Task<DeliverySettingsConfig> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetCachedSettings());
    }

    public async Task<DeliverySettingsConfig> SaveSettingsAsync(
        DeliverySettingsConfig settings,
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
            LauncherApiBaseUrlEu: defaultEuLauncherApiBaseUrl);
    }

    private static DeliverySettingsConfig NormalizeSettings(DeliverySettingsConfig settings)
    {
        var normalizedPublicBaseUrl = NormalizeAbsoluteUrl(settings.PublicBaseUrl);
        var normalizedAssetBaseUrl = NormalizeAbsoluteUrl(settings.AssetBaseUrl);
        var normalizedLauncherApiBaseUrlRu = NormalizeAbsoluteUrl(settings.LauncherApiBaseUrlRu);
        var normalizedLauncherApiBaseUrlEu = NormalizeAbsoluteUrl(settings.LauncherApiBaseUrlEu);
        var normalizedFallbacks = (settings.FallbackApiBaseUrls ?? [])
            .Select(NormalizeAbsoluteUrl)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(value =>
                !string.Equals(value, normalizedPublicBaseUrl, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, normalizedAssetBaseUrl, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new DeliverySettingsConfig(
            PublicBaseUrl: normalizedPublicBaseUrl,
            AssetBaseUrl: normalizedAssetBaseUrl,
            FallbackApiBaseUrls: normalizedFallbacks,
            UpdatedAtUtc: DateTime.UtcNow,
            LauncherApiBaseUrlRu: normalizedLauncherApiBaseUrlRu,
            LauncherApiBaseUrlEu: normalizedLauncherApiBaseUrlEu);
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
