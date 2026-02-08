namespace BivLauncher.Api.Services;

public interface ILauncherUpdateConfigProvider
{
    Task<LauncherUpdateConfig?> GetAsync(CancellationToken cancellationToken = default);
    Task<LauncherUpdateConfig> SaveAsync(LauncherUpdateConfig config, CancellationToken cancellationToken = default);
}
