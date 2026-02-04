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
[Route("api/admin/settings/auth-provider")]
public sealed class AdminAuthProviderSettingsController(
    AppDbContext dbContext,
    IOptions<AuthProviderOptions> fallbackOptions,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AuthProviderSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var stored = await dbContext.AuthProviderConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            var fallback = fallbackOptions.Value;
            return Ok(new AuthProviderSettingsDto(
                fallback.LoginUrl,
                Math.Clamp(fallback.TimeoutSeconds, 5, 120),
                fallback.AllowDevFallback,
                null));
        }

        return Ok(new AuthProviderSettingsDto(
            stored.LoginUrl,
            Math.Clamp(stored.TimeoutSeconds, 5, 120),
            stored.AllowDevFallback,
            stored.UpdatedAtUtc));
    }

    [HttpPut]
    public async Task<ActionResult<AuthProviderSettingsDto>> Put(
        [FromBody] AuthProviderSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.AuthProviderConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new AuthProviderConfig();
            dbContext.AuthProviderConfigs.Add(config);
        }

        config.LoginUrl = request.LoginUrl.Trim();
        config.TimeoutSeconds = Math.Clamp(request.TimeoutSeconds, 5, 120);
        config.AllowDevFallback = request.AllowDevFallback;
        config.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.auth-provider.update",
            actor: actor,
            entityType: "settings",
            entityId: "auth-provider",
            details: new
            {
                config.LoginUrl,
                config.TimeoutSeconds,
                config.AllowDevFallback
            },
            cancellationToken: cancellationToken);

        return Ok(new AuthProviderSettingsDto(
            config.LoginUrl,
            config.TimeoutSeconds,
            config.AllowDevFallback,
            config.UpdatedAtUtc));
    }
}
