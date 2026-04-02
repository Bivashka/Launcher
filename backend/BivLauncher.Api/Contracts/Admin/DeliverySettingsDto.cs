namespace BivLauncher.Api.Contracts.Admin;

public sealed record DeliverySettingsDto(
    string PublicBaseUrl,
    string AssetBaseUrl,
    IReadOnlyList<string> FallbackApiBaseUrls,
    string LauncherApiBaseUrlRu,
    string LauncherApiBaseUrlEu,
    DateTime? UpdatedAtUtc);
