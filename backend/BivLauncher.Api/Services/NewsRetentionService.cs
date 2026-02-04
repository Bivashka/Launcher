using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Services;

public sealed class NewsRetentionService(
    AppDbContext dbContext,
    IOptions<NewsRetentionOptions> fallbackOptions,
    ILogger<NewsRetentionService> logger) : INewsRetentionService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly NewsRetentionOptions _fallbackOptions = fallbackOptions.Value;
    private readonly ILogger<NewsRetentionService> _logger = logger;

    public async Task<NewsRetentionDryRunResponse> PreviewRetentionAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var plan = await BuildPlanAsync(cancellationToken);

        return new NewsRetentionDryRunResponse(
            Enabled: plan.Settings.Enabled,
            MaxItems: plan.Settings.MaxItems,
            MaxAgeDays: plan.Settings.MaxAgeDays,
            TotalItems: plan.TotalItems,
            WouldDeleteByAge: plan.OldIds.Count,
            WouldDeleteByOverflow: plan.OverflowIds.Count,
            WouldDeleteTotal: plan.IdsToDelete.Count,
            WouldRemainItems: Math.Max(0, plan.TotalItems - plan.IdsToDelete.Count),
            CalculatedAtUtc: now);
    }

    public async Task<NewsRetentionRunResponse> ApplyRetentionAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var plan = await BuildPlanAsync(cancellationToken);
        var config = plan.Settings.Config;

        if (!plan.Settings.Enabled)
        {
            if (config is not null)
            {
                config.LastAppliedAtUtc = now;
                config.LastDeletedItems = 0;
                config.LastError = string.Empty;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new NewsRetentionRunResponse(
                Applied: false,
                DeletedItems: 0,
                RemainingItems: plan.TotalItems,
                AppliedAtUtc: now,
                Error: string.Empty);
        }

        try
        {
            if (plan.IdsToDelete.Count > 0)
            {
                var entities = await _dbContext.NewsItems
                    .Where(x => plan.IdsToDelete.Contains(x.Id))
                    .ToListAsync(cancellationToken);

                _dbContext.NewsItems.RemoveRange(entities);
            }

            if (config is not null)
            {
                config.LastAppliedAtUtc = now;
                config.LastDeletedItems = plan.IdsToDelete.Count;
                config.LastError = string.Empty;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            var remaining = await _dbContext.NewsItems.CountAsync(cancellationToken);

            _logger.LogInformation(
                "News retention applied. Deleted {DeletedItems}, Remaining {RemainingItems}, MaxItems {MaxItems}, MaxAgeDays {MaxAgeDays}",
                plan.IdsToDelete.Count,
                remaining,
                plan.Settings.MaxItems,
                plan.Settings.MaxAgeDays);

            return new NewsRetentionRunResponse(
                Applied: true,
                DeletedItems: plan.IdsToDelete.Count,
                RemainingItems: remaining,
                AppliedAtUtc: now,
                Error: string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "News retention apply failed.");
            if (config is not null)
            {
                config.LastAppliedAtUtc = now;
                config.LastDeletedItems = 0;
                config.LastError = Truncate(ex.Message, 1024);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var remaining = await _dbContext.NewsItems.CountAsync(cancellationToken);
            return new NewsRetentionRunResponse(
                Applied: false,
                DeletedItems: 0,
                RemainingItems: remaining,
                AppliedAtUtc: now,
                Error: "News retention failed.");
        }
    }

    private async Task<RetentionPlan> BuildPlanAsync(CancellationToken cancellationToken)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        var totalItems = await _dbContext.NewsItems.CountAsync(cancellationToken);

        if (!settings.Enabled)
        {
            return new RetentionPlan(
                Settings: settings,
                TotalItems: totalItems,
                OldIds: [],
                OverflowIds: [],
                IdsToDelete: new HashSet<Guid>());
        }

        var cutoff = DateTime.UtcNow.AddDays(-settings.MaxAgeDays);
        var oldIds = await _dbContext.NewsItems
            .AsNoTracking()
            .Where(x => !x.Pinned && x.CreatedAtUtc < cutoff)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var overflowIds = await _dbContext.NewsItems
            .AsNoTracking()
            .Where(x => !x.Pinned)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(settings.MaxItems)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var idsToDelete = new HashSet<Guid>();
        foreach (var id in oldIds)
        {
            idsToDelete.Add(id);
        }
        foreach (var id in overflowIds)
        {
            idsToDelete.Add(id);
        }

        return new RetentionPlan(
            Settings: settings,
            TotalItems: totalItems,
            OldIds: oldIds,
            OverflowIds: overflowIds,
            IdsToDelete: idsToDelete);
    }

    private async Task<RetentionSettings> ResolveSettingsAsync(CancellationToken cancellationToken)
    {
        var config = await _dbContext.NewsRetentionConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var enabled = config?.Enabled ?? _fallbackOptions.Enabled;
        var maxItems = Math.Clamp(config?.MaxItems ?? _fallbackOptions.MaxItems, 50, 10000);
        var maxAgeDays = Math.Clamp(config?.MaxAgeDays ?? _fallbackOptions.MaxAgeDays, 1, 3650);

        return new RetentionSettings(enabled, maxItems, maxAgeDays, config);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private sealed record RetentionSettings(
        bool Enabled,
        int MaxItems,
        int MaxAgeDays,
        NewsRetentionConfig? Config);

    private sealed record RetentionPlan(
        RetentionSettings Settings,
        int TotalItems,
        IReadOnlyList<Guid> OldIds,
        IReadOnlyList<Guid> OverflowIds,
        IReadOnlySet<Guid> IdsToDelete);
}
