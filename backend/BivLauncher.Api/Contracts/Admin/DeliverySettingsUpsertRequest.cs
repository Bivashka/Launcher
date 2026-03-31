using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class DeliverySettingsUpsertRequest
{
    [MaxLength(1024)]
    public string PublicBaseUrl { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string AssetBaseUrl { get; set; } = string.Empty;

    public List<string> FallbackApiBaseUrls { get; set; } = [];
}
