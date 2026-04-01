using BivLauncher.Api.Services;

namespace BivLauncher.Api.Tests;

internal sealed class StubDeliverySettingsProvider(DeliverySettingsConfig? initial = null) : IDeliverySettingsProvider
{
    private DeliverySettingsConfig _settings = initial ?? new DeliverySettingsConfig(
        PublicBaseUrl: "https://cdn.local",
        AssetBaseUrl: "https://cdn.local",
        FallbackApiBaseUrls: [],
        UpdatedAtUtc: null);

    public DeliverySettingsConfig GetCachedSettings()
    {
        return _settings;
    }

    public Task<DeliverySettingsConfig> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings);
    }

    public Task<DeliverySettingsConfig> SaveSettingsAsync(
        DeliverySettingsConfig settings,
        CancellationToken cancellationToken = default)
    {
        _settings = settings;
        return Task.FromResult(_settings);
    }
}

internal sealed class StubSecuritySettingsProvider(SecuritySettingsConfig? initial = null) : ISecuritySettingsProvider
{
    private SecuritySettingsConfig _settings = initial ?? new SecuritySettingsConfig(
        MaxConcurrentGameAccountsPerDevice: 1,
        LauncherAdminUsernames: [],
        GameSessionHeartbeatIntervalSeconds: 45,
        GameSessionExpirationSeconds: 150,
        UpdatedAtUtc: null);

    public SecuritySettingsConfig GetCachedSettings()
    {
        return _settings;
    }

    public Task<SecuritySettingsConfig> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings);
    }

    public Task<SecuritySettingsConfig> SaveSettingsAsync(
        SecuritySettingsConfig settings,
        CancellationToken cancellationToken = default)
    {
        _settings = settings;
        return Task.FromResult(_settings);
    }
}
