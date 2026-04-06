using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings/security")]
public sealed class AdminSecuritySettingsController(
    AppDbContext dbContext,
    ISecuritySettingsProvider securitySettingsProvider,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SecuritySettingsDto>> Get(CancellationToken cancellationToken)
    {
        var settings = await securitySettingsProvider.GetSettingsAsync(cancellationToken);
        return Ok(Map(settings));
    }

    [HttpPut]
    public async Task<ActionResult<SecuritySettingsDto>> Put(
        [FromBody] SecuritySettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var current = await securitySettingsProvider.GetSettingsAsync(cancellationToken);
        var saved = await securitySettingsProvider.SaveSettingsAsync(
            current with
            {
                MaxConcurrentGameAccountsPerDevice = request.MaxConcurrentGameAccountsPerDevice,
                LauncherAdminUsernames = [.. request.LauncherAdminUsernames],
                SiteCosmeticsUploadSecret = request.SiteCosmeticsUploadSecret,
                UpdatedAtUtc = DateTime.UtcNow
            },
            cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.security.update",
            actor: actor,
            entityType: "settings",
            entityId: "security",
            details: new
            {
                saved.MaxConcurrentGameAccountsPerDevice,
                saved.LauncherAdminUsernames,
                hasSiteCosmeticsUploadSecret = !string.IsNullOrWhiteSpace(saved.SiteCosmeticsUploadSecret),
                saved.GameSessionHeartbeatIntervalSeconds,
                saved.GameSessionExpirationSeconds
            },
            cancellationToken: cancellationToken);

        return Ok(Map(saved));
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<ActiveGameSessionDto>>> Sessions(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await PruneExpiredSessionsAsync(now, cancellationToken);

        var sessions = await dbContext.ActiveGameSessions
            .AsNoTracking()
            .OrderByDescending(x => x.LastHeartbeatAtUtc)
            .Select(x => new ActiveGameSessionDto(
                x.Id,
                x.AccountId,
                x.Username,
                x.HwidHash,
                x.DeviceUserName,
                x.ServerId,
                x.ServerName,
                x.StartedAtUtc,
                x.LastHeartbeatAtUtc,
                x.ExpiresAtUtc,
                x.ExpiresAtUtc > now))
            .ToListAsync(cancellationToken);

        return Ok(sessions);
    }

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> DeleteSession(Guid id, CancellationToken cancellationToken)
    {
        var session = await dbContext.ActiveGameSessions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null)
        {
            return NotFound();
        }

        dbContext.ActiveGameSessions.Remove(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "security.game-session.delete",
            actor: actor,
            entityType: "game-session",
            entityId: id.ToString(),
            details: new
            {
                session.AccountId,
                session.Username,
                session.HwidHash,
                session.DeviceUserName,
                session.ServerId,
                session.ServerName
            },
            cancellationToken: cancellationToken);

        return NoContent();
    }

    private async Task PruneExpiredSessionsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var expiredSessions = await dbContext.ActiveGameSessions
            .Where(x => x.ExpiresAtUtc <= nowUtc)
            .ToListAsync(cancellationToken);
        if (expiredSessions.Count == 0)
        {
            return;
        }

        dbContext.ActiveGameSessions.RemoveRange(expiredSessions);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static SecuritySettingsDto Map(SecuritySettingsConfig config)
    {
        return new SecuritySettingsDto(
            config.MaxConcurrentGameAccountsPerDevice,
            config.LauncherAdminUsernames,
            config.SiteCosmeticsUploadSecret,
            config.GameSessionHeartbeatIntervalSeconds,
            config.GameSessionExpirationSeconds,
            config.UpdatedAtUtc);
    }
}
