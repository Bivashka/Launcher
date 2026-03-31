namespace BivLauncher.Api.Services;

public sealed record DeliverySettingsConfig(
    string PublicBaseUrl,
    string AssetBaseUrl,
    IReadOnlyList<string> FallbackApiBaseUrls,
    DateTime? UpdatedAtUtc);
