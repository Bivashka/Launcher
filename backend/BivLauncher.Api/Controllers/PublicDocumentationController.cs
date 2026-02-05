using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Route("api/public/docs")]
public sealed class PublicDocumentationController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicDocumentationArticleDto>>> List(
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedSearch = (search ?? string.Empty).Trim();
        var normalizedCategory = (category ?? string.Empty).Trim().ToLowerInvariant();

        var query = dbContext.DocumentationArticles
            .AsNoTracking()
            .Where(x => x.Published);

        if (!string.IsNullOrWhiteSpace(normalizedCategory))
        {
            query = query.Where(x => x.Category == normalizedCategory);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(x =>
                x.Title.Contains(normalizedSearch) ||
                x.Summary.Contains(normalizedSearch) ||
                x.BodyMarkdown.Contains(normalizedSearch));
        }

        var items = await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Order)
            .ThenBy(x => x.Title)
            .Select(x => new PublicDocumentationArticleDto(
                x.Slug,
                x.Title,
                x.Category,
                x.Summary,
                x.BodyMarkdown,
                x.Order,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<PublicDocumentationArticleDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var normalizedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return NotFound();
        }

        var item = await dbContext.DocumentationArticles
            .AsNoTracking()
            .Where(x => x.Published && x.Slug == normalizedSlug)
            .Select(x => new PublicDocumentationArticleDto(
                x.Slug,
                x.Title,
                x.Category,
                x.Summary,
                x.BodyMarkdown,
                x.Order,
                x.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }
}
