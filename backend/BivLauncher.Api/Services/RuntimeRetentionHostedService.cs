using BivLauncher.Api.Data;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Services;

public sealed class RuntimeRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<RuntimeRetentionOptions> fallbackOptions,
    ILogger<RuntimeRetentionHostedService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly RuntimeRetentionOptions _fallbackOptions = fallbackOptions.Value;
    private readonly ILogger<RuntimeRetentionHostedService> _logger = logger;
    private DateTime? _lastFallbackRunAtUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Runtime retention background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Runtime retention loop failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var retentionService = scope.ServiceProvider.GetRequiredService<IRuntimeRetentionService>();

        var config = await dbContext.RuntimeRetentionConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var enabled = config?.Enabled ?? _fallbackOptions.Enabled;
        var intervalMinutes = Math.Clamp(config?.IntervalMinutes ?? _fallbackOptions.IntervalMinutes, 5, 10080);
        var lastRunAtUtc = config?.LastRunAtUtc ?? _lastFallbackRunAtUtc;

        if (!enabled)
        {
            return;
        }

        if (lastRunAtUtc.HasValue && DateTime.UtcNow - lastRunAtUtc.Value < TimeSpan.FromMinutes(intervalMinutes))
        {
            return;
        }

        var result = await retentionService.ApplyRetentionAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            _logger.LogWarning("Runtime retention run completed with error: {Error}", result.Error);
        }
        else
        {
            _logger.LogInformation(
                "Runtime retention run completed. Profiles {ProfilesProcessed}, Deleted {DeletedItems}",
                result.ProfilesProcessed,
                result.DeletedItems);
        }

        if (config is null)
        {
            _lastFallbackRunAtUtc = DateTime.UtcNow;
        }
    }
}
