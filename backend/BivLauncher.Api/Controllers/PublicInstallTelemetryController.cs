using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Infrastructure;
using BivLauncher.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Controllers;

[ApiController]
[EnableRateLimiting(RateLimitPolicies.PublicIngestPolicy)]
[Route("api/public/install-telemetry")]
public sealed class PublicInstallTelemetryController(
    AppDbContext dbContext,
    IOptions<InstallTelemetryOptions> fallbackOptions) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PublicInstallTelemetryTrackResponse>> Track(
        [FromBody] PublicInstallTelemetryTrackRequest request,
        CancellationToken cancellationToken)
    {
        var enabled = await ResolveInstallTelemetryEnabledAsync(cancellationToken);
        var now = DateTime.UtcNow;

        if (!enabled)
        {
            return Ok(new PublicInstallTelemetryTrackResponse(
                Accepted: false,
                Enabled: false,
                ProcessedAtUtc: now));
        }

        var projectName = NormalizeProjectName(request.ProjectName);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return BadRequest(new { error = "ProjectName is required." });
        }

        var launcherVersion = NormalizeLauncherVersion(request.LauncherVersion);
        var projectKey = projectName.ToLowerInvariant();

        var stat = await dbContext.ProjectInstallStats
            .FirstOrDefaultAsync(x => x.ProjectKey == projectKey, cancellationToken);

        if (stat is null)
        {
            stat = new ProjectInstallStat
            {
                ProjectKey = projectKey,
                ProjectName = projectName,
                LastLauncherVersion = launcherVersion,
                SeenCount = 1,
                FirstSeenAtUtc = now,
                LastSeenAtUtc = now
            };
            dbContext.ProjectInstallStats.Add(stat);
        }
        else
        {
            stat.ProjectName = projectName;
            stat.LastLauncherVersion = launcherVersion;
            stat.SeenCount = Math.Max(1, stat.SeenCount + 1);
            stat.LastSeenAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new PublicInstallTelemetryTrackResponse(
            Accepted: true,
            Enabled: true,
            ProcessedAtUtc: now));
    }

    private async Task<bool> ResolveInstallTelemetryEnabledAsync(CancellationToken cancellationToken)
    {
        var stored = await dbContext.InstallTelemetryConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => (bool?)x.Enabled)
            .FirstOrDefaultAsync(cancellationToken);

        return stored ?? fallbackOptions.Value.Enabled;
    }

    private static string NormalizeProjectName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        return trimmed.Length <= 128 ? trimmed : trimmed[..128];
    }

    private static string NormalizeLauncherVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "unknown";
        }

        var trimmed = raw.Trim();
        return trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }
}
