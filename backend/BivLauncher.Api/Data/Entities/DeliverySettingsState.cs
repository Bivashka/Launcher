namespace BivLauncher.Api.Data.Entities;

public sealed class DeliverySettingsState
{
    public int Id { get; set; }
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string AssetBaseUrl { get; set; } = string.Empty;
    public string FallbackApiBaseUrlsJson { get; set; } = "[]";
    public string LauncherApiBaseUrlRu { get; set; } = string.Empty;
    public string LauncherApiBaseUrlEu { get; set; } = string.Empty;
    public string PublicBaseUrlRu { get; set; } = string.Empty;
    public string PublicBaseUrlEu { get; set; } = string.Empty;
    public string AssetBaseUrlRu { get; set; } = string.Empty;
    public string AssetBaseUrlEu { get; set; } = string.Empty;
    public string FallbackApiBaseUrlsRuJson { get; set; } = "[]";
    public string FallbackApiBaseUrlsEuJson { get; set; } = "[]";
    public DateTime? UpdatedAtUtc { get; set; }
}
