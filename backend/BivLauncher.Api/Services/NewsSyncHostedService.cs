using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Services;

public sealed class NewsSyncHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<NewsSyncOptions> fallbackOptions,
    ILogger<NewsSyncHostedService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly NewsSyncOptions _fallbackOptions = fallbackOptions.Value;
    private readonly ILogger<NewsSyncHostedService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("News sync background service started.");

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
                _logger.LogError(ex, "News sync loop failed.");
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
        var importService = scope.ServiceProvider.GetRequiredService<INewsImportService>();

        var config = await dbContext.NewsSyncConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var enabled = config?.Enabled ?? _fallbackOptions.Enabled;
        var intervalMinutes = Math.Clamp(config?.IntervalMinutes ?? _fallbackOptions.IntervalMinutes, 5, 1440);
        var lastRunAtUtc = config?.LastRunAtUtc;

        if (!enabled)
        {
            return;
        }

        if (lastRunAtUtc.HasValue && DateTime.UtcNow - lastRunAtUtc.Value < TimeSpan.FromMinutes(intervalMinutes))
        {
            return;
        }

        try
        {
            var result = await importService.SyncAsync(sourceId: null, force: false, cancellationToken);
            _logger.LogInformation(
                "News auto-sync completed. Sources: {SourcesProcessed}, Imported: {Imported}",
                result.SourcesProcessed,
                result.Imported);

            if (config is not null)
            {
                config.LastRunAtUtc = DateTime.UtcNow;
                config.LastRunError = string.Empty;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "News auto-sync failed.");

            if (config is not null)
            {
                config.LastRunAtUtc = DateTime.UtcNow;
                config.LastRunError = Truncate(ex.Message, 1024);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
