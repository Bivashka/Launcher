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
[Route("api/admin/news")]
public sealed class AdminNewsController(
    AppDbContext dbContext,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NewsItemDto>>> List(
        [FromQuery] bool includeDisabled = true,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.NewsItems.AsNoTracking().AsQueryable();
        if (!includeDisabled)
        {
            query = query.Where(x => x.Enabled);
        }

        var items = await query
            .OrderByDescending(x => x.Pinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new NewsItemDto(
                x.Id,
                x.Title,
                x.Body,
                x.Source,
                x.Pinned,
                x.Enabled,
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NewsItemDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.NewsItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new NewsItemDto(
                x.Id,
                x.Title,
                x.Body,
                x.Source,
                x.Pinned,
                x.Enabled,
                x.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<NewsItemDto>> Create([FromBody] NewsUpsertRequest request, CancellationToken cancellationToken)
    {
        var title = request.Title.Trim();
        var body = request.Body.Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new { error = "Title and body are required." });
        }

        var item = new NewsItem
        {
            Title = title,
            Body = body,
            Source = NormalizeSource(request.Source),
            Pinned = request.Pinned,
            Enabled = request.Enabled,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.NewsItems.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "news.create",
            actor: actor,
            entityType: "news",
            entityId: item.Id.ToString(),
            details: new
            {
                item.Source,
                item.Pinned,
                item.Enabled
            },
            cancellationToken: cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, Map(item));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<NewsItemDto>> Update(Guid id, [FromBody] NewsUpsertRequest request, CancellationToken cancellationToken)
    {
        var item = await dbContext.NewsItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var title = request.Title.Trim();
        var body = request.Body.Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new { error = "Title and body are required." });
        }

        item.Title = title;
        item.Body = body;
        item.Source = NormalizeSource(request.Source);
        item.Pinned = request.Pinned;
        item.Enabled = request.Enabled;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "news.update",
            actor: actor,
            entityType: "news",
            entityId: item.Id.ToString(),
            details: new
            {
                item.Source,
                item.Pinned,
                item.Enabled
            },
            cancellationToken: cancellationToken);

        return Ok(Map(item));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.NewsItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        dbContext.NewsItems.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "news.delete",
            actor: actor,
            entityType: "news",
            entityId: item.Id.ToString(),
            details: new
            {
                item.Source,
                item.Pinned
            },
            cancellationToken: cancellationToken);

        return NoContent();
    }

    private static string NormalizeSource(string source)
    {
        return string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim();
    }

    private static NewsItemDto Map(NewsItem item)
    {
        return new NewsItemDto(
            item.Id,
            item.Title,
            item.Body,
            item.Source,
            item.Pinned,
            item.Enabled,
            item.CreatedAtUtc);
    }
}
