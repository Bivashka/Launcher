namespace BivLauncher.Api.Services;

public interface IDeliverySettingsProvider
{
    DeliverySettingsConfig GetCachedSettings();

    Task<DeliverySettingsConfig> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<DeliverySettingsConfig> SaveSettingsAsync(
        DeliverySettingsConfig settings,
        CancellationToken cancellationToken = default);
}
