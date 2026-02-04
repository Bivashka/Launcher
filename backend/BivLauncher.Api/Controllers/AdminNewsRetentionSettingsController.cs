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
[Route("api/admin/settings/news-retention")]
public sealed class AdminNewsRetentionSettingsController(
    AppDbContext dbContext,
    IOptions<NewsRetentionOptions> fallbackOptions,
    INewsRetentionService newsRetentionService,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NewsRetentionSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var stored = await dbContext.NewsRetentionConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            var fallback = fallbackOptions.Value;
            return Ok(new NewsRetentionSettingsDto(
                fallback.Enabled,
                Math.Clamp(fallback.MaxItems, 50, 10000),
                Math.Clamp(fallback.MaxAgeDays, 1, 3650),
                null,
                0,
                string.Empty,
                null));
        }

        return Ok(Map(stored));
    }

    [HttpPut]
    public async Task<ActionResult<NewsRetentionSettingsDto>> Put(
        [FromBody] NewsRetentionSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.NewsRetentionConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new NewsRetentionConfig();
            dbContext.NewsRetentionConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.MaxItems = Math.Clamp(request.MaxItems, 50, 10000);
        config.MaxAgeDays = Math.Clamp(request.MaxAgeDays, 1, 3650);
        config.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "news.retention.settings.update",
            actor: actor,
            entityType: "settings",
            entityId: "news-retention",
            details: new
            {
                config.Enabled,
                config.MaxItems,
                config.MaxAgeDays
            },
            cancellationToken: cancellationToken);

        return Ok(Map(config));
    }

    [HttpPost("run")]
    public async Task<ActionResult<NewsRetentionRunResponse>> RunNow(CancellationToken cancellationToken)
    {
        var result = await newsRetentionService.ApplyRetentionAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "news.retention.run",
            actor: actor,
            entityType: "news-retention",
            entityId: "global",
            details: new
            {
                result.Applied,
                result.DeletedItems,
                result.RemainingItems,
                result.Error
            },
            cancellationToken: cancellationToken);

        return Ok(result);
    }

    [HttpPost("dry-run")]
    public async Task<ActionResult<NewsRetentionDryRunResponse>> DryRun(CancellationToken cancellationToken)
    {
        var result = await newsRetentionService.PreviewRetentionAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "news.retention.dry-run",
            actor: actor,
            entityType: "news-retention",
            entityId: "global",
            details: new
            {
                result.Enabled,
                result.MaxItems,
                result.MaxAgeDays,
                result.WouldDeleteTotal,
                result.WouldRemainItems
            },
            cancellationToken: cancellationToken);

        return Ok(result);
    }

    private static NewsRetentionSettingsDto Map(NewsRetentionConfig config)
    {
        return new NewsRetentionSettingsDto(
            config.Enabled,
            config.MaxItems,
            config.MaxAgeDays,
            config.LastAppliedAtUtc,
            config.LastDeletedItems,
            config.LastError,
            config.UpdatedAtUtc);
    }
}
