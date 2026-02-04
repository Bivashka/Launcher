namespace BivLauncher.Api.Services;

public sealed class AssetUrlService(IConfiguration configuration) : IAssetUrlService
{
    public string BuildPublicUrl(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var baseUrl = configuration["PUBLIC_BASE_URL"]
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
