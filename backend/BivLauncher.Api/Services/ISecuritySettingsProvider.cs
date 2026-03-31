namespace BivLauncher.Api.Services;

public interface ISecuritySettingsProvider
{
    SecuritySettingsConfig GetCachedSettings();

    Task<SecuritySettingsConfig> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<SecuritySettingsConfig> SaveSettingsAsync(
        SecuritySettingsConfig settings,
        CancellationToken cancellationToken = default);
}
