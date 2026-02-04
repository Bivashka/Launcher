using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Services;

public sealed class RuntimeRetentionService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IOptions<RuntimeRetentionOptions> fallbackOptions,
    ILogger<RuntimeRetentionService> logger) : IRuntimeRetentionService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IObjectStorageService _objectStorageService = objectStorageService;
    private readonly RuntimeRetentionOptions _fallbackOptions = fallbackOptions.Value;
    private readonly ILogger<RuntimeRetentionService> _logger = logger;

    public async Task<RuntimeRetentionDryRunResponse> PreviewRetentionAsync(
        string? profileSlug = null,
        int maxProfiles = 20,
        int previewKeysLimit = 10,
        CancellationToken cancellationToken = default)
    {
        var normalizedProfileSlug = NormalizeProfileSlug(profileSlug);
        var normalizedMaxProfiles = Math.Clamp(maxProfiles, 1, 200);
        var normalizedPreviewKeysLimit = Math.Clamp(previewKeysLimit, 1, 100);
        var settings = await ResolveSettingsAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return new RuntimeRetentionDryRunResponse(
                Enabled: false,
                IntervalMinutes: settings.IntervalMinutes,
                KeepLast: settings.KeepLast,
                ProfileSlugFilter: normalizedProfileSlug,
                MaxProfiles: normalizedMaxProfiles,
                PreviewKeysLimit: normalizedPreviewKeysLimit,
                ProfilesScanned: 0,
                ProfilesWithDeletions: 0,
                ProfilesReturned: 0,
                HasMoreProfiles: false,
                TotalDeleteCandidates: 0,
                Profiles: [],
                CalculatedAtUtc: DateTime.UtcNow);
        }

        var batch = await BuildPlanBatchAsync(
            settings.KeepLast,
            normalizedProfileSlug,
            normalizedMaxProfiles,
            cancellationToken);

        var previewProfiles = batch.SelectedPlans
            .Select(plan => new RuntimeRetentionProfileDryRunItem(
                ProfileSlug: plan.ProfileSlug,
                TotalRuntimeObjects: plan.OrderedKeys.Count,
                KeepCount: plan.KeepKeys.Count,
                DeleteCount: plan.DeleteKeys.Count,
                DeleteKeysPreview: plan.DeleteKeys.Take(normalizedPreviewKeysLimit).ToList(),
                HasMoreDeleteKeys: plan.DeleteKeys.Count > normalizedPreviewKeysLimit))
            .ToList();

        return new RuntimeRetentionDryRunResponse(
            Enabled: true,
            IntervalMinutes: settings.IntervalMinutes,
            KeepLast: settings.KeepLast,
            ProfileSlugFilter: normalizedProfileSlug,
            MaxProfiles: normalizedMaxProfiles,
            PreviewKeysLimit: normalizedPreviewKeysLimit,
            ProfilesScanned: batch.ProfilesScanned,
            ProfilesWithDeletions: batch.WithDeletions.Count,
            ProfilesReturned: previewProfiles.Count,
            HasMoreProfiles: batch.WithDeletions.Count > previewProfiles.Count,
            TotalDeleteCandidates: batch.WithDeletions.Sum(x => x.DeleteKeys.Count),
            Profiles: previewProfiles,
            CalculatedAtUtc: DateTime.UtcNow);
    }

    public async Task<RuntimeRetentionRunResponse> ApplyRetentionFromPreviewAsync(
        string? profileSlug = null,
        int maxProfiles = 20,
        CancellationToken cancellationToken = default)
    {
        var normalizedProfileSlug = NormalizeProfileSlug(profileSlug);
        var normalizedMaxProfiles = Math.Clamp(maxProfiles, 1, 200);
        var now = DateTime.UtcNow;
        var settings = await ResolveSettingsAsync(cancellationToken);
        var config = settings.Config;
        if (!settings.Enabled)
        {
            if (config is not null)
            {
                config.LastRunAtUtc = now;
                config.LastDeletedItems = 0;
                config.LastProfilesProcessed = 0;
                config.LastRunError = string.Empty;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new RuntimeRetentionRunResponse(
                Applied: false,
                ProfilesProcessed: 0,
                DeletedItems: 0,
                AppliedAtUtc: now,
                Error: string.Empty);
        }

        try
        {
            var batch = await BuildPlanBatchAsync(
                settings.KeepLast,
                normalizedProfileSlug,
                normalizedMaxProfiles,
                cancellationToken);

            var deletedItems = 0;
            foreach (var plan in batch.SelectedPlans)
            {
                foreach (var deleteKey in plan.DeleteKeys)
                {
                    await _objectStorageService.DeleteAsync(deleteKey, cancellationToken);
                }

                deletedItems += plan.DeleteKeys.Count;
            }

            if (config is not null)
            {
                config.LastRunAtUtc = now;
                config.LastDeletedItems = deletedItems;
                config.LastProfilesProcessed = batch.SelectedPlans.Count;
                config.LastRunError = string.Empty;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new RuntimeRetentionRunResponse(
                Applied: true,
                ProfilesProcessed: batch.SelectedPlans.Count,
                DeletedItems: deletedItems,
                AppliedAtUtc: now,
                Error: string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Runtime retention apply-from-preview failed.");
            if (config is not null)
            {
                config.LastRunAtUtc = now;
                config.LastDeletedItems = 0;
                config.LastProfilesProcessed = 0;
                config.LastRunError = Truncate(ex.Message, 1024);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new RuntimeRetentionRunResponse(
                Applied: false,
                ProfilesProcessed: 0,
                DeletedItems: 0,
                AppliedAtUtc: now,
                Error: "Runtime retention failed.");
        }
    }

    public async Task<RuntimeRetentionRunResponse> ApplyRetentionAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var settings = await ResolveSettingsAsync(cancellationToken);
        var config = settings.Config;
        if (!settings.Enabled)
        {
            if (config is not null)
            {
                config.LastRunAtUtc = now;
                config.LastDeletedItems = 0;
                config.LastProfilesProcessed = 0;
                config.LastRunError = string.Empty;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new RuntimeRetentionRunResponse(
                Applied: false,
                ProfilesProcessed: 0,
                DeletedItems: 0,
                AppliedAtUtc: now,
                Error: string.Empty);
        }

        try
        {
            var profiles = await _dbContext.Profiles
                .AsNoTracking()
                .Select(x => new { x.Slug, x.BundledRuntimeKey })
                .ToListAsync(cancellationToken);

            var deletedItems = 0;
            foreach (var profile in profiles)
            {
                var plan = await BuildProfilePlanAsync(
                    profile.Slug,
                    profile.BundledRuntimeKey,
                    settings.KeepLast,
                    cancellationToken);

                foreach (var deleteKey in plan.DeleteKeys)
                {
                    await _objectStorageService.DeleteAsync(deleteKey, cancellationToken);
                }

                deletedItems += plan.DeleteKeys.Count;
            }

            if (config is not null)
            {
                config.LastRunAtUtc = now;
                config.LastDeletedItems = deletedItems;
                config.LastProfilesProcessed = profiles.Count;
                config.LastRunError = string.Empty;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Runtime retention applied. Profiles {ProfilesProcessed}, Deleted {DeletedItems}, KeepLast {KeepLast}",
                profiles.Count,
                deletedItems,
                settings.KeepLast);

            return new RuntimeRetentionRunResponse(
                Applied: true,
                ProfilesProcessed: profiles.Count,
                DeletedItems: deletedItems,
                AppliedAtUtc: now,
                Error: string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Runtime retention apply failed.");
            if (config is not null)
            {
                config.LastRunAtUtc = now;
                config.LastDeletedItems = 0;
                config.LastProfilesProcessed = 0;
                config.LastRunError = Truncate(ex.Message, 1024);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return new RuntimeRetentionRunResponse(
                Applied: false,
                ProfilesProcessed: 0,
                DeletedItems: 0,
                AppliedAtUtc: now,
                Error: "Runtime retention failed.");
        }
    }

    private async Task<ProfileCleanupPlan> BuildProfilePlanAsync(
        string profileSlug,
        string profileRuntimeKey,
        int keepLast,
        CancellationToken cancellationToken)
    {
        var normalizedSlug = profileSlug.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return new ProfileCleanupPlan(string.Empty, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);
        }

        var runtimePrefix = $"runtimes/{normalizedSlug}/";
        var runtimeObjects = await _objectStorageService.ListByPrefixAsync(runtimePrefix, cancellationToken);
        if (runtimeObjects.Count == 0)
        {
            return new ProfileCleanupPlan(normalizedSlug, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);
        }

        var orderedKeys = runtimeObjects
            .OrderByDescending(x => x.LastModifiedUtc)
            .ThenByDescending(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => NormalizeStorageKey(x.Key))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keepKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedProfileRuntimeKey = NormalizeStorageKey(profileRuntimeKey);
        if (!string.IsNullOrWhiteSpace(normalizedProfileRuntimeKey))
        {
            keepKeys.Add(normalizedProfileRuntimeKey);
        }

        foreach (var key in orderedKeys)
        {
            if (keepKeys.Count >= keepLast + (string.IsNullOrWhiteSpace(normalizedProfileRuntimeKey) ? 0 : 1))
            {
                break;
            }

            keepKeys.Add(key);
        }

        var deleteKeys = orderedKeys
            .Where(x => !keepKeys.Contains(x))
            .ToList();

        return new ProfileCleanupPlan(normalizedSlug, orderedKeys, keepKeys, deleteKeys);
    }

    private async Task<PlanBatch> BuildPlanBatchAsync(
        int keepLast,
        string normalizedProfileSlug,
        int maxProfiles,
        CancellationToken cancellationToken)
    {
        var profilesQuery = _dbContext.Profiles
            .AsNoTracking()
            .Select(x => new { x.Slug, x.BundledRuntimeKey });

        if (!string.IsNullOrWhiteSpace(normalizedProfileSlug))
        {
            profilesQuery = profilesQuery.Where(x => x.Slug == normalizedProfileSlug);
        }

        var profiles = await profilesQuery.ToListAsync(cancellationToken);
        var plans = new List<ProfileCleanupPlan>(profiles.Count);
        foreach (var profile in profiles)
        {
            plans.Add(await BuildProfilePlanAsync(
                profile.Slug,
                profile.BundledRuntimeKey,
                keepLast,
                cancellationToken));
        }

        var withDeletions = plans
            .Where(x => x.DeleteKeys.Count > 0)
            .OrderByDescending(x => x.DeleteKeys.Count)
            .ThenBy(x => x.ProfileSlug, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selected = withDeletions.Take(maxProfiles).ToList();
        return new PlanBatch(profiles.Count, withDeletions, selected);
    }

    private async Task<RetentionSettings> ResolveSettingsAsync(CancellationToken cancellationToken)
    {
        var config = await _dbContext.RuntimeRetentionConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var enabled = config?.Enabled ?? _fallbackOptions.Enabled;
        var intervalMinutes = Math.Clamp(config?.IntervalMinutes ?? _fallbackOptions.IntervalMinutes, 5, 10080);
        var keepLast = Math.Clamp(config?.KeepLast ?? _fallbackOptions.KeepLast, 0, 100);
        return new RetentionSettings(enabled, intervalMinutes, keepLast, config);
    }

    private static string NormalizeStorageKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key)
            ? string.Empty
            : key.Trim().Replace('\\', '/').TrimStart('/');
    }

    private static string NormalizeProfileSlug(string? profileSlug)
    {
        return string.IsNullOrWhiteSpace(profileSlug)
            ? string.Empty
            : profileSlug.Trim().ToLowerInvariant();
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
        int IntervalMinutes,
        int KeepLast,
        RuntimeRetentionConfig? Config);

    private sealed record ProfileCleanupPlan(
        string ProfileSlug,
        IReadOnlyList<string> OrderedKeys,
        IReadOnlySet<string> KeepKeys,
        IReadOnlyList<string> DeleteKeys);

    private sealed record PlanBatch(
        int ProfilesScanned,
        IReadOnlyList<ProfileCleanupPlan> WithDeletions,
        IReadOnlyList<ProfileCleanupPlan> SelectedPlans);
}
