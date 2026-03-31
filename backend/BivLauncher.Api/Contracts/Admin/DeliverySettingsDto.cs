namespace BivLauncher.Api.Contracts.Admin;

public sealed record DeliverySettingsDto(
    string PublicBaseUrl,
    string AssetBaseUrl,
    IReadOnlyList<string> FallbackApiBaseUrls,
    DateTime? UpdatedAtUtc);
