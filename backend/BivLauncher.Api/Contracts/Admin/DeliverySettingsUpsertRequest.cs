using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class DeliverySettingsUpsertRequest
{
    [MaxLength(1024)]
    public string PublicBaseUrl { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string AssetBaseUrl { get; set; } = string.Empty;

    public List<string> FallbackApiBaseUrls { get; set; } = [];

    [MaxLength(1024)]
    public string LauncherApiBaseUrlRu { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string LauncherApiBaseUrlEu { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string PublicBaseUrlRu { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string PublicBaseUrlEu { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string AssetBaseUrlRu { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string AssetBaseUrlEu { get; set; } = string.Empty;

    public List<string> FallbackApiBaseUrlsRu { get; set; } = [];

    public List<string> FallbackApiBaseUrlsEu { get; set; } = [];
}
