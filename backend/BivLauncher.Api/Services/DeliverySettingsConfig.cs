namespace BivLauncher.Api.Services;

public sealed record DeliverySettingsConfig(
    string PublicBaseUrl,
    string AssetBaseUrl,
    IReadOnlyList<string> FallbackApiBaseUrls,
    DateTime? UpdatedAtUtc,
    string LauncherApiBaseUrlRu = "",
    string LauncherApiBaseUrlEu = "",
    string PublicBaseUrlRu = "",
    string PublicBaseUrlEu = "",
    string AssetBaseUrlRu = "",
    string AssetBaseUrlEu = "",
    IReadOnlyList<string>? FallbackApiBaseUrlsRu = null,
    IReadOnlyList<string>? FallbackApiBaseUrlsEu = null);
