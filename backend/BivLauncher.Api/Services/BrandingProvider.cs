using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class BrandingProvider(
    IWebHostEnvironment environment,
    IOptions<BrandingOptions> options,
    ILogger<BrandingProvider> logger) : IBrandingProvider
{
    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly BrandingConfig DefaultBranding = new(
        ProductName: "BivLauncher",
        DeveloperName: "Bivashka",
        Tagline: "Managed launcher platform",
        SupportUrl: "https://example.com/support",
        PrimaryColor: "#2F6FED",
        AccentColor: "#20C997",
        LogoText: "BLP",
        BackgroundImageUrl: string.Empty,
        BackgroundOverlayOpacity: 0.55,
        LoginCardPosition: "center",
        LoginCardWidth: 460);

    public async Task<BrandingConfig> GetBrandingAsync(CancellationToken cancellationToken = default)
    {
        var brandingPath = GetBrandingPath();

        if (!File.Exists(brandingPath))
        {
            logger.LogWarning("Branding file {BrandingPath} not found. Falling back to defaults.", brandingPath);
            return DefaultBranding;
        }

        await using var stream = File.OpenRead(brandingPath);
        var config = await JsonSerializer.DeserializeAsync<BrandingConfig>(
            stream,
            ReadJsonOptions,
            cancellationToken);
        return config ?? DefaultBranding;
    }

    public async Task<BrandingConfig> SaveBrandingAsync(BrandingConfig branding, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeBranding(branding);
        var brandingPath = GetBrandingPath();
        var directory = Path.GetDirectoryName(brandingPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(brandingPath);
        await JsonSerializer.SerializeAsync(stream, normalized, WriteJsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return normalized;
    }

    private string GetBrandingPath()
    {
        var configuredPath = options.Value.FilePath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
    }

    private static BrandingConfig NormalizeBranding(BrandingConfig branding)
    {
        return new BrandingConfig(
            ProductName: NormalizeValue(branding.ProductName, DefaultBranding.ProductName),
            DeveloperName: NormalizeValue(branding.DeveloperName, DefaultBranding.DeveloperName),
            Tagline: NormalizeValue(branding.Tagline, DefaultBranding.Tagline),
            SupportUrl: NormalizeValue(branding.SupportUrl, DefaultBranding.SupportUrl),
            PrimaryColor: NormalizeValue(branding.PrimaryColor, DefaultBranding.PrimaryColor),
            AccentColor: NormalizeValue(branding.AccentColor, DefaultBranding.AccentColor),
            LogoText: NormalizeValue(branding.LogoText, DefaultBranding.LogoText),
            BackgroundImageUrl: NormalizeOptionalValue(branding.BackgroundImageUrl),
            BackgroundOverlayOpacity: ClampDouble(branding.BackgroundOverlayOpacity, 0, 0.95, DefaultBranding.BackgroundOverlayOpacity),
            LoginCardPosition: NormalizeLoginCardPosition(branding.LoginCardPosition),
            LoginCardWidth: ClampInt(branding.LoginCardWidth, 340, 640, DefaultBranding.LoginCardWidth));
    }

    private static string NormalizeValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static double ClampDouble(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static int ClampInt(int value, int min, int max, int fallback)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static string NormalizeLoginCardPosition(string? position)
    {
        var normalized = (position ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "left" => "left",
            "right" => "right",
            _ => "center"
        };
    }
}
