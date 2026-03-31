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
            .ToListAsync(cancellationToken);

        var scopeNames = await ResolveScopeNamesAsync(cancellationToken);
        return Ok(items.Select(item => Map(item, scopeNames)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NewsItemDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.NewsItems
            .AsNoTracking()
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        var scopeNames = await ResolveScopeNamesAsync(cancellationToken);
        return Ok(Map(item, scopeNames));
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

        var normalizedScope = await NormalizeScopeAsync(request.ScopeType, request.ScopeId, cancellationToken);
        if (normalizedScope is null)
        {
            return BadRequest(new { error = "Invalid news scope." });
        }

        var item = new NewsItem
        {
            Title = title,
            Body = body,
            Source = NormalizeSource(request.Source),
            ScopeType = normalizedScope.Value.ScopeType,
            ScopeId = normalizedScope.Value.ScopeId,
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
                item.ScopeType,
                item.ScopeId,
                item.Pinned,
                item.Enabled
            },
            cancellationToken: cancellationToken);

        var scopeNames = await ResolveScopeNamesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, Map(item, scopeNames));
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

        var normalizedScope = await NormalizeScopeAsync(request.ScopeType, request.ScopeId, cancellationToken);
        if (normalizedScope is null)
        {
            return BadRequest(new { error = "Invalid news scope." });
        }

        item.Title = title;
        item.Body = body;
        item.Source = NormalizeSource(request.Source);
        item.ScopeType = normalizedScope.Value.ScopeType;
        item.ScopeId = normalizedScope.Value.ScopeId;
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
                item.ScopeType,
                item.ScopeId,
                item.Pinned,
                item.Enabled
            },
            cancellationToken: cancellationToken);

        var scopeNames = await ResolveScopeNamesAsync(cancellationToken);
        return Ok(Map(item, scopeNames));
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

    private async Task<Dictionary<string, string>> ResolveScopeNamesAsync(CancellationToken cancellationToken)
    {
        var profiles = await dbContext.Profiles
            .AsNoTracking()
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);
        var servers = await dbContext.Servers
            .AsNoTracking()
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var scopeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            scopeNames[$"profile:{profile.Id}"] = profile.Name;
        }

        foreach (var server in servers)
        {
            scopeNames[$"server:{server.Id}"] = server.Name;
        }

        return scopeNames;
    }

    private async Task<(string ScopeType, string ScopeId)?> NormalizeScopeAsync(
        string? rawScopeType,
        string? rawScopeId,
        CancellationToken cancellationToken)
    {
        var scopeType = NormalizeScopeType(rawScopeType);
        var scopeId = (rawScopeId ?? string.Empty).Trim();

        if (scopeType == "global")
        {
            return (scopeType, string.Empty);
        }

        if (!Guid.TryParse(scopeId, out var parsedScopeId))
        {
            return null;
        }

        var exists = scopeType == "profile"
            ? await dbContext.Profiles.AnyAsync(x => x.Id == parsedScopeId, cancellationToken)
            : await dbContext.Servers.AnyAsync(x => x.Id == parsedScopeId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return (scopeType, parsedScopeId.ToString());
    }

    private static string NormalizeScopeType(string? rawScopeType)
    {
        var normalized = (rawScopeType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "profile" => "profile",
            "server" => "server",
            _ => "global"
        };
    }

    private static NewsItemDto Map(NewsItem item, IReadOnlyDictionary<string, string> scopeNames)
    {
        var scopeType = NormalizeScopeType(item.ScopeType);
        var scopeId = (item.ScopeId ?? string.Empty).Trim();
        var scopeKey = string.IsNullOrWhiteSpace(scopeId)
            ? scopeType
            : $"{scopeType}:{scopeId}";
        var scopeName = scopeType switch
        {
            "profile" when scopeNames.TryGetValue(scopeKey, out var profileName) => profileName,
            "server" when scopeNames.TryGetValue(scopeKey, out var serverName) => serverName,
            "profile" => "Profile",
            "server" => "Server",
            _ => "Global"
        };

        return new NewsItemDto(
            item.Id,
            item.Title,
            item.Body,
            item.Source,
            scopeType,
            scopeId,
            scopeName,
            item.Pinned,
            item.Enabled,
            item.CreatedAtUtc);
    }
}
