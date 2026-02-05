using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/install-telemetry")]
public sealed class AdminInstallTelemetryController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet("projects")]
    public async Task<ActionResult<IReadOnlyList<ProjectInstallStatDto>>> ListProjects(
        [FromQuery] string search = "",
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedSearch = search.Trim();
        var take = Math.Clamp(limit, 1, 1000);

        var query = dbContext.ProjectInstallStats
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(x => EF.Functions.ILike(x.ProjectName, $"%{normalizedSearch}%"));
        }

        var items = await query
            .OrderByDescending(x => x.LastSeenAtUtc)
            .ThenBy(x => x.ProjectName)
            .Take(take)
            .Select(x => new ProjectInstallStatDto(
                x.Id,
                x.ProjectName,
                x.LastLauncherVersion,
                x.SeenCount,
                x.FirstSeenAtUtc,
                x.LastSeenAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
