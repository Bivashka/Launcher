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
[Route("api/admin/discord-rpc")]
public sealed class AdminDiscordRpcController(
    AppDbContext dbContext,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet("profile/{profileId:guid}")]
    public Task<ActionResult<DiscordRpcConfigDto>> GetProfileConfig(Guid profileId, CancellationToken cancellationToken)
    {
        return GetByScopeAsync("profile", profileId, cancellationToken);
    }

    [HttpPut("profile/{profileId:guid}")]
    public Task<ActionResult<DiscordRpcConfigDto>> PutProfileConfig(
        Guid profileId,
        [FromBody] DiscordRpcUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return UpsertByScopeAsync("profile", profileId, request, cancellationToken);
    }

    [HttpDelete("profile/{profileId:guid}")]
    public Task<IActionResult> DeleteProfileConfig(Guid profileId, CancellationToken cancellationToken)
    {
        return DeleteByScopeAsync("profile", profileId, cancellationToken);
    }

    [HttpGet("server/{serverId:guid}")]
    public Task<ActionResult<DiscordRpcConfigDto>> GetServerConfig(Guid serverId, CancellationToken cancellationToken)
    {
        return GetByScopeAsync("server", serverId, cancellationToken);
    }

    [HttpPut("server/{serverId:guid}")]
    public Task<ActionResult<DiscordRpcConfigDto>> PutServerConfig(
        Guid serverId,
        [FromBody] DiscordRpcUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return UpsertByScopeAsync("server", serverId, request, cancellationToken);
    }

    [HttpDelete("server/{serverId:guid}")]
    public Task<IActionResult> DeleteServerConfig(Guid serverId, CancellationToken cancellationToken)
    {
        return DeleteByScopeAsync("server", serverId, cancellationToken);
    }

    private async Task<ActionResult<DiscordRpcConfigDto>> GetByScopeAsync(
        string scopeType,
        Guid scopeId,
        CancellationToken cancellationToken)
    {
        var exists = await ScopeExists(scopeType, scopeId, cancellationToken);
        if (!exists)
        {
            return NotFound(new { error = $"{scopeType} not found." });
        }

        var config = await dbContext.DiscordRpcConfigs
            .AsNoTracking()
            .Where(x => x.ScopeType == scopeType && x.ScopeId == scopeId)
            .Select(x => Map(x))
            .FirstOrDefaultAsync(cancellationToken);

        return config is null ? NotFound() : Ok(config);
    }

    private async Task<ActionResult<DiscordRpcConfigDto>> UpsertByScopeAsync(
        string scopeType,
        Guid scopeId,
        DiscordRpcUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await ScopeExists(scopeType, scopeId, cancellationToken);
        if (!exists)
        {
            return NotFound(new { error = $"{scopeType} not found." });
        }

        var config = await dbContext.DiscordRpcConfigs
            .FirstOrDefaultAsync(x => x.ScopeType == scopeType && x.ScopeId == scopeId, cancellationToken);

        if (config is null)
        {
            config = new DiscordRpcConfig
            {
                ScopeType = scopeType,
                ScopeId = scopeId
            };

            dbContext.DiscordRpcConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.AppId = request.AppId.Trim();
        config.DetailsText = request.DetailsText.Trim();
        config.StateText = request.StateText.Trim();
        config.LargeImageKey = request.LargeImageKey.Trim();
        config.LargeImageText = request.LargeImageText.Trim();
        config.SmallImageKey = request.SmallImageKey.Trim();
        config.SmallImageText = request.SmallImageText.Trim();
        config.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "discord-rpc.upsert",
            actor: actor,
            entityType: scopeType,
            entityId: scopeId.ToString(),
            details: new
            {
                config.Enabled,
                hasAppId = !string.IsNullOrWhiteSpace(config.AppId)
            },
            cancellationToken: cancellationToken);

        return Ok(Map(config));
    }

    private async Task<IActionResult> DeleteByScopeAsync(
        string scopeType,
        Guid scopeId,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.DiscordRpcConfigs
            .FirstOrDefaultAsync(x => x.ScopeType == scopeType && x.ScopeId == scopeId, cancellationToken);

        if (config is null)
        {
            return NotFound();
        }

        dbContext.DiscordRpcConfigs.Remove(config);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "discord-rpc.delete",
            actor: actor,
            entityType: scopeType,
            entityId: scopeId.ToString(),
            details: new { },
            cancellationToken: cancellationToken);

        return NoContent();
    }

    private async Task<bool> ScopeExists(string scopeType, Guid scopeId, CancellationToken cancellationToken)
    {
        return scopeType switch
        {
            "profile" => await dbContext.Profiles.AnyAsync(x => x.Id == scopeId, cancellationToken),
            "server" => await dbContext.Servers.AnyAsync(x => x.Id == scopeId, cancellationToken),
            _ => false
        };
    }

    private static DiscordRpcConfigDto Map(DiscordRpcConfig config)
    {
        return new DiscordRpcConfigDto(
            config.Id,
            config.ScopeType,
            config.ScopeId,
            config.Enabled,
            config.AppId,
            config.DetailsText,
            config.StateText,
            config.LargeImageKey,
            config.LargeImageText,
            config.SmallImageKey,
            config.SmallImageText,
            config.UpdatedAtUtc);
    }
}
