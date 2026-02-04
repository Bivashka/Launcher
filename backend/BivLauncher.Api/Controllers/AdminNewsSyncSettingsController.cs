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
[Route("api/admin/settings/news-sync")]
public sealed class AdminNewsSyncSettingsController(
    AppDbContext dbContext,
    IOptions<NewsSyncOptions> fallbackOptions,
    INewsImportService newsImportService,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NewsSyncSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var stored = await dbContext.NewsSyncConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            var fallback = fallbackOptions.Value;
            return Ok(new NewsSyncSettingsDto(
                fallback.Enabled,
                Math.Clamp(fallback.IntervalMinutes, 5, 1440),
                null,
                string.Empty,
                null));
        }

        return Ok(Map(stored));
    }

    [HttpPut]
    public async Task<ActionResult<NewsSyncSettingsDto>> Put(
        [FromBody] NewsSyncSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.NewsSyncConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new NewsSyncConfig();
            dbContext.NewsSyncConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.IntervalMinutes = Math.Clamp(request.IntervalMinutes, 5, 1440);
        config.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "news.sync.settings.update",
            actor: actor,
            entityType: "settings",
            entityId: "news-sync",
            details: new
            {
                config.Enabled,
                config.IntervalMinutes
            },
            cancellationToken: cancellationToken);

        return Ok(Map(config));
    }

    [HttpPost("run")]
    public async Task<ActionResult<NewsSourcesSyncResponse>> RunNow(CancellationToken cancellationToken)
    {
        var config = await dbContext.NewsSyncConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            var fallback = fallbackOptions.Value;
            config = new NewsSyncConfig
            {
                Enabled = fallback.Enabled,
                IntervalMinutes = Math.Clamp(fallback.IntervalMinutes, 5, 1440)
            };

            dbContext.NewsSyncConfigs.Add(config);
        }

        try
        {
            var result = await newsImportService.SyncAsync(sourceId: null, cancellationToken);

            config.LastRunAtUtc = DateTime.UtcNow;
            config.LastRunError = string.Empty;
            config.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "news.sync.run",
                actor: actor,
                entityType: "news-sync",
                entityId: "all-sources",
                details: new
                {
                    result.SourcesProcessed,
                    result.Imported,
                    failedSources = result.Results.Count(x => !string.IsNullOrWhiteSpace(x.Error))
                },
                cancellationToken: cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            config.LastRunAtUtc = DateTime.UtcNow;
            config.LastRunError = Truncate(ex.Message, 1024);
            config.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "news.sync.run.failed",
                actor: actor,
                entityType: "news-sync",
                entityId: "all-sources",
                details: new
                {
                    error = Truncate(ex.Message, 512)
                },
                cancellationToken: cancellationToken);

            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "News run failed." });
        }
    }

    private static NewsSyncSettingsDto Map(NewsSyncConfig config)
    {
        return new NewsSyncSettingsDto(
            config.Enabled,
            config.IntervalMinutes,
            config.LastRunAtUtc,
            config.LastRunError,
            config.UpdatedAtUtc);
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
