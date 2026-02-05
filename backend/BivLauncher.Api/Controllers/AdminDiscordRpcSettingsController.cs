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
[Route("api/admin/settings/discord-rpc")]
public sealed class AdminDiscordRpcSettingsController(
    AppDbContext dbContext,
    IOptions<DiscordRpcOptions> fallbackOptions,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DiscordRpcSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var stored = await dbContext.DiscordRpcGlobalConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            var fallback = fallbackOptions.Value;
            return Ok(new DiscordRpcSettingsDto(
                Enabled: fallback.Enabled,
                PrivacyMode: fallback.PrivacyMode,
                UpdatedAtUtc: null));
        }

        return Ok(new DiscordRpcSettingsDto(
            stored.Enabled,
            stored.PrivacyMode,
            stored.UpdatedAtUtc));
    }

    [HttpPut]
    public async Task<ActionResult<DiscordRpcSettingsDto>> Put(
        [FromBody] DiscordRpcSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.DiscordRpcGlobalConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new DiscordRpcGlobalConfig();
            dbContext.DiscordRpcGlobalConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.PrivacyMode = request.PrivacyMode;
        config.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.discord-rpc.update",
            actor: actor,
            entityType: "settings",
            entityId: "discord-rpc",
            details: new
            {
                config.Enabled,
                config.PrivacyMode
            },
            cancellationToken: cancellationToken);

        return Ok(new DiscordRpcSettingsDto(
            config.Enabled,
            config.PrivacyMode,
            config.UpdatedAtUtc));
    }
}
