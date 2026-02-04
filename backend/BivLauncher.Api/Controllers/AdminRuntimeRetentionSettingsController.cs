using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings/runtime-retention")]
public sealed class AdminRuntimeRetentionSettingsController(
    AppDbContext dbContext,
    IOptions<RuntimeRetentionOptions> fallbackOptions,
    IRuntimeRetentionService runtimeRetentionService,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RuntimeRetentionSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var stored = await dbContext.RuntimeRetentionConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            var fallback = fallbackOptions.Value;
            return Ok(new RuntimeRetentionSettingsDto(
                fallback.Enabled,
                Math.Clamp(fallback.IntervalMinutes, 5, 10080),
                Math.Clamp(fallback.KeepLast, 0, 100),
                null,
                0,
                0,
                string.Empty,
                null));
        }

        return Ok(Map(stored));
    }

    [HttpPut]
    public async Task<ActionResult<RuntimeRetentionSettingsDto>> Put(
        [FromBody] RuntimeRetentionSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.RuntimeRetentionConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new RuntimeRetentionConfig();
            dbContext.RuntimeRetentionConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.IntervalMinutes = Math.Clamp(request.IntervalMinutes, 5, 10080);
        config.KeepLast = Math.Clamp(request.KeepLast, 0, 100);
        config.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "runtime.retention.settings.update",
            actor: actor,
            entityType: "runtime-retention",
            entityId: "global",
            details: new
            {
                config.Enabled,
                config.IntervalMinutes,
                config.KeepLast
            },
            cancellationToken: cancellationToken);
        return Ok(Map(config));
    }

    [HttpPost("run")]
    public async Task<ActionResult<RuntimeRetentionRunResponse>> RunNow(CancellationToken cancellationToken)
    {
        var config = await dbContext.RuntimeRetentionConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            var fallback = fallbackOptions.Value;
            config = new RuntimeRetentionConfig
            {
                Enabled = fallback.Enabled,
                IntervalMinutes = Math.Clamp(fallback.IntervalMinutes, 5, 10080),
                KeepLast = Math.Clamp(fallback.KeepLast, 0, 100)
            };

            dbContext.RuntimeRetentionConfigs.Add(config);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var result = await runtimeRetentionService.ApplyRetentionAsync(cancellationToken);
        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "runtime.retention.run",
            actor: actor,
            entityType: "runtime-retention",
            entityId: "global",
            details: new
            {
                result.Applied,
                result.ProfilesProcessed,
                result.DeletedItems,
                result.Error
            },
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, result);
        }

        return Ok(result);
    }

    [HttpPost("run-from-preview")]
    public async Task<ActionResult<RuntimeRetentionRunResponse>> RunFromPreview(
        [FromQuery] string? profileSlug = null,
        [FromQuery] int maxProfiles = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await runtimeRetentionService.ApplyRetentionFromPreviewAsync(
            profileSlug,
            maxProfiles,
            cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "runtime.retention.run-from-preview",
            actor: actor,
            entityType: "runtime-retention",
            entityId: string.IsNullOrWhiteSpace(profileSlug) ? "all-profiles" : profileSlug.Trim().ToLowerInvariant(),
            details: new
            {
                profileSlug = (profileSlug ?? string.Empty).Trim().ToLowerInvariant(),
                maxProfiles,
                result.Applied,
                result.ProfilesProcessed,
                result.DeletedItems,
                result.Error
            },
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, result);
        }

        return Ok(result);
    }

    [HttpPost("dry-run")]
    public async Task<ActionResult<RuntimeRetentionDryRunResponse>> DryRun(
        [FromQuery] string? profileSlug = null,
        [FromQuery] int maxProfiles = 20,
        [FromQuery] int previewKeysLimit = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await runtimeRetentionService.PreviewRetentionAsync(
            profileSlug,
            maxProfiles,
            previewKeysLimit,
            cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        var normalizedProfileSlug = string.IsNullOrWhiteSpace(profileSlug)
            ? string.Empty
            : profileSlug.Trim().ToLowerInvariant();
        await auditService.WriteAsync(
            action: "runtime.retention.dry-run",
            actor: actor,
            entityType: "runtime-retention",
            entityId: string.IsNullOrWhiteSpace(normalizedProfileSlug) ? "all-profiles" : normalizedProfileSlug,
            details: new
            {
                profileSlug = normalizedProfileSlug,
                maxProfiles,
                previewKeysLimit,
                result.ProfilesScanned,
                result.ProfilesWithDeletions,
                result.ProfilesReturned,
                result.TotalDeleteCandidates
            },
            cancellationToken: cancellationToken);

        return Ok(result);
    }

    private static RuntimeRetentionSettingsDto Map(RuntimeRetentionConfig config)
    {
        return new RuntimeRetentionSettingsDto(
            config.Enabled,
            config.IntervalMinutes,
            config.KeepLast,
            config.LastRunAtUtc,
            config.LastDeletedItems,
            config.LastProfilesProcessed,
            config.LastRunError,
            config.UpdatedAtUtc);
    }
}
