namespace BivLauncher.Api.Services;

public sealed class AssetUrlService(
    IConfiguration configuration,
    IDeliverySettingsProvider deliverySettingsProvider) : IAssetUrlService
{
    public string BuildPublicUrl(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var deliverySettings = deliverySettingsProvider.GetCachedSettings();
        var baseUrl = !string.IsNullOrWhiteSpace(deliverySettings.AssetBaseUrl)
            ? deliverySettings.AssetBaseUrl
            : !string.IsNullOrWhiteSpace(deliverySettings.PublicBaseUrl)
                ? deliverySettings.PublicBaseUrl
                : configuration["PUBLIC_BASE_URL"]
                    ?? configuration["PublicBaseUrl"]
                    ?? "http://localhost:8080";

        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        var escapedKey = string.Join('/',
            key.Trim('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        return $"{normalizedBaseUrl}/api/public/assets/{escapedKey}";
    }
}
