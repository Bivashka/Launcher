namespace BivLauncher.Api.Contracts.Admin;

public sealed record DeliverySettingsDto(
    string PublicBaseUrl,
    string AssetBaseUrl,
    IReadOnlyList<string> FallbackApiBaseUrls,
    string LauncherApiBaseUrlRu,
    string LauncherApiBaseUrlEu,
    string PublicBaseUrlRu,
    string PublicBaseUrlEu,
    string AssetBaseUrlRu,
    string AssetBaseUrlEu,
    IReadOnlyList<string> FallbackApiBaseUrlsRu,
    IReadOnlyList<string> FallbackApiBaseUrlsEu,
    DateTime? UpdatedAtUtc);
