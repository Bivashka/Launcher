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
[Route("api/admin/settings/install-telemetry")]
public sealed class AdminInstallTelemetrySettingsController(
    AppDbContext dbContext,
    IOptions<InstallTelemetryOptions> fallbackOptions,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<InstallTelemetrySettingsDto>> Get(CancellationToken cancellationToken)
    {
        var stored = await dbContext.InstallTelemetryConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return Ok(new InstallTelemetrySettingsDto(
                Enabled: fallbackOptions.Value.Enabled,
                UpdatedAtUtc: null));
        }

        return Ok(new InstallTelemetrySettingsDto(
            Enabled: stored.Enabled,
            UpdatedAtUtc: stored.UpdatedAtUtc));
    }

    [HttpPut]
    public async Task<ActionResult<InstallTelemetrySettingsDto>> Put(
        [FromBody] InstallTelemetrySettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.InstallTelemetryConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new InstallTelemetryConfig();
            dbContext.InstallTelemetryConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.install-telemetry.update",
            actor: actor,
            entityType: "settings",
            entityId: "install-telemetry",
            details: new
            {
                config.Enabled
            },
            cancellationToken: cancellationToken);

        return Ok(new InstallTelemetrySettingsDto(config.Enabled, config.UpdatedAtUtc));
    }
}
