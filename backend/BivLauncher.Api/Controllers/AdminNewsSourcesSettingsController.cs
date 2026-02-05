using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings/news-sources")]
public sealed class AdminNewsSourcesSettingsController(
    AppDbContext dbContext,
    INewsImportService newsImportService,
    IAdminAuditService auditService) : ControllerBase
{
    private static readonly HashSet<string> AllowedTypes = ["rss", "json", "markdown", "telegram", "vk"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NewsSourceSettingsDto>>> Get(CancellationToken cancellationToken)
    {
        var sources = await dbContext.NewsSourceConfigs
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(sources.Select(Map).ToList());
    }

    [HttpPut]
    public async Task<ActionResult<IReadOnlyList<NewsSourceSettingsDto>>> Put(
        [FromBody] NewsSourcesUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var items = request.Sources ?? [];
        var existing = await dbContext.NewsSourceConfigs
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var existingById = existing.ToDictionary(x => x.Id);
        var keepIds = new HashSet<Guid>();
        var now = DateTime.UtcNow;
        var createdCount = 0;
        var updatedCount = 0;

        foreach (var item in items)
        {
            var name = item.Name.Trim();
            var type = item.Type.Trim().ToLowerInvariant();
            var url = item.Url.Trim();
            var maxItems = Math.Clamp(item.MaxItems, 1, 20);
            var minFetchIntervalMinutes = Math.Clamp(item.MinFetchIntervalMinutes, 1, 1440);

            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { error = "Source name is required." });
            }

            if (!AllowedTypes.Contains(type))
            {
                return BadRequest(new { error = $"Unsupported source type '{item.Type}'." });
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                return BadRequest(new { error = $"Source URL is invalid for '{name}'." });
            }

            NewsSourceConfig target;
            if (item.Id.HasValue && existingById.TryGetValue(item.Id.Value, out var existingSource))
            {
                target = existingSource;
                updatedCount++;
            }
            else
            {
                target = new NewsSourceConfig();
                dbContext.NewsSourceConfigs.Add(target);
                createdCount++;
            }

            target.Name = name;
            target.Type = type;
            target.Url = url;
            target.Enabled = item.Enabled;
            target.MaxItems = maxItems;
            target.MinFetchIntervalMinutes = minFetchIntervalMinutes;
            target.UpdatedAtUtc = now;
            keepIds.Add(target.Id);
        }

        foreach (var source in existing)
        {
            if (!keepIds.Contains(source.Id))
            {
                dbContext.NewsSourceConfigs.Remove(source);
            }
        }
        var deletedCount = existing.Count(x => !keepIds.Contains(x.Id));

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "news.sources.update",
            actor: actor,
            entityType: "settings",
            entityId: "news-sources",
            details: new
            {
                totalSubmitted = items.Count,
                createdCount,
                updatedCount,
                deletedCount
            },
            cancellationToken: cancellationToken);

        var saved = await dbContext.NewsSourceConfigs
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(saved.Select(Map).ToList());
    }

    [HttpPost("sync")]
    public async Task<ActionResult<NewsSourcesSyncResponse>> Sync(
        [FromQuery] Guid? sourceId,
        CancellationToken cancellationToken,
        [FromQuery] bool force = false)
    {
        if (sourceId.HasValue)
        {
            var exists = await dbContext.NewsSourceConfigs
                .AsNoTracking()
                .AnyAsync(x => x.Id == sourceId.Value, cancellationToken);

            if (!exists)
            {
                return NotFound(new { error = "News source not found." });
            }
        }

        var summary = await newsImportService.SyncAsync(sourceId, force, cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "news.sources.sync",
            actor: actor,
            entityType: "news-source",
            entityId: sourceId?.ToString() ?? "all",
            details: new
            {
                sourceId,
                force,
                summary.SourcesProcessed,
                summary.Imported,
                failedSources = summary.Results.Count(x => !string.IsNullOrWhiteSpace(x.Error))
            },
            cancellationToken: cancellationToken);

        return Ok(summary);
    }

    private static NewsSourceSettingsDto Map(NewsSourceConfig source)
    {
        return new NewsSourceSettingsDto(
            source.Id,
            source.Name,
            source.Type,
            source.Url,
            source.Enabled,
            source.MaxItems,
            source.MinFetchIntervalMinutes,
            source.LastFetchAttemptAtUtc,
            source.LastSyncAtUtc,
            source.LastContentChangeAtUtc,
            source.LastSyncError,
            source.UpdatedAtUtc);
    }
}
