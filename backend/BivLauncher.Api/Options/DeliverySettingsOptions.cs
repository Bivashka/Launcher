namespace BivLauncher.Api.Options;

public sealed class DeliverySettingsOptions
{
    public const string SectionName = "Delivery";

    public string FilePath { get; set; } = "delivery.json";
}
