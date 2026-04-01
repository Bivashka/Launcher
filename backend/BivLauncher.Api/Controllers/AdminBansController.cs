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
[Route("api/admin/bans")]
public sealed class AdminBansController(
    AppDbContext dbContext,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BanDto>>> List(
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var query = dbContext.HardwareBans
            .AsNoTracking()
            .Include(x => x.Account)
            .AsQueryable();

        if (activeOnly)
        {
            query = query.Where(x => x.ExpiresAtUtc == null || x.ExpiresAtUtc > now);
        }

        var bans = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new BanDto(
                x.Id,
                x.AccountId,
                x.Account != null ? x.Account.Username : string.Empty,
                x.Account != null ? x.Account.ExternalId : string.Empty,
                x.HwidHash,
                x.DeviceUserName,
                x.Reason,
                x.CreatedAtUtc,
                x.ExpiresAtUtc,
                x.ExpiresAtUtc == null || x.ExpiresAtUtc > now))
            .ToListAsync(cancellationToken);

        return Ok(bans);
    }

    [HttpPost("hwid")]
    public async Task<ActionResult<BanDto>> BanHwid([FromBody] HwidBanCreateRequest request, CancellationToken cancellationToken)
    {
        var hwidHash = request.HwidHash.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(hwidHash))
        {
            return BadRequest(new { error = "HWID hash is required." });
        }

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            return BadRequest(new { error = "ExpiresAtUtc must be in the future." });
        }

        var now = DateTime.UtcNow;
        var alreadyActive = await dbContext.HardwareBans.AnyAsync(
            x => x.HwidHash == hwidHash && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now),
            cancellationToken);
        if (alreadyActive)
        {
            return Conflict(new { error = "Active HWID ban already exists." });
        }

        var ban = new HardwareBan
        {
            HwidHash = hwidHash,
            DeviceUserName = string.Empty,
            Reason = NormalizeReason(request.Reason),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = request.ExpiresAtUtc
        };

        var nowUtc = DateTime.UtcNow;
        var matchedAccounts = await dbContext.AuthAccounts
            .Where(x => x.HwidHash == hwidHash)
            .ToListAsync(cancellationToken);
        foreach (var matchedAccount in matchedAccounts)
        {
            matchedAccount.SessionVersion++;
            matchedAccount.UpdatedAtUtc = nowUtc;
        }

        dbContext.HardwareBans.Add(ban);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "ban.hwid.create",
            actor: actor,
            entityType: "ban",
            entityId: ban.Id.ToString(),
            details: new
            {
                ban.HwidHash,
                revokedSessionsForAccounts = matchedAccounts.Count,
                ban.ExpiresAtUtc
            },
            cancellationToken: cancellationToken);

        return Ok(Map(ban, null, null));
    }

    [HttpPost("account/{user}")]
    public async Task<ActionResult<BanDto>> BanAccount(
        string user,
        [FromBody] AccountBanCreateRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedUser = user.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            return BadRequest(new { error = "User is required." });
        }

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            return BadRequest(new { error = "ExpiresAtUtc must be in the future." });
        }

        var resolvedAccount = await ResolveAccountByUserAsync(normalizedUser, cancellationToken);
        if (resolvedAccount is null)
        {
            return NotFound(new { error = "Account not found." });
        }

        var account = resolvedAccount.Account;

        var now = DateTime.UtcNow;
        var alreadyActive = await dbContext.HardwareBans.AnyAsync(
            x => x.AccountId == account.Id && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now),
            cancellationToken);
        if (alreadyActive)
        {
            return Conflict(new { error = "Active account ban already exists." });
        }

        var ban = new HardwareBan
        {
            AccountId = account.Id,
            HwidHash = resolvedAccount.HwidHash,
            DeviceUserName = resolvedAccount.DeviceUserName,
            Reason = NormalizeReason(request.Reason),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = request.ExpiresAtUtc
        };

        if (request.ExpiresAtUtc is null)
        {
            account.Banned = true;
        }
        account.SessionVersion++;
        account.UpdatedAtUtc = DateTime.UtcNow;

        dbContext.HardwareBans.Add(ban);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "ban.account.create",
            actor: actor,
            entityType: "ban",
            entityId: ban.Id.ToString(),
            details: new
            {
                accountId = account.Id,
                account.Username,
                account.ExternalId,
                account.SessionVersion,
                ban.HwidHash,
                ban.DeviceUserName,
                ban.ExpiresAtUtc
            },
            cancellationToken: cancellationToken);

        return Ok(Map(ban, account.Username, account.ExternalId));
    }

    [HttpPost("account/{user}/reset-hwid")]
    public async Task<IActionResult> ResetAccountHwid(string user, CancellationToken cancellationToken)
    {
        var normalizedUser = user.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            return BadRequest(new { error = "User is required." });
        }

        var resolvedAccount = await ResolveAccountByUserAsync(normalizedUser, cancellationToken);
        if (resolvedAccount is null)
        {
            return NotFound(new { error = "Account not found." });
        }

        var account = resolvedAccount.Account;

        account.HwidHash = string.Empty;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "ban.account.reset-hwid",
            actor: actor,
            entityType: "account",
            entityId: account.ExternalId,
            details: new
            {
                accountId = account.Id,
                account.Username
            },
            cancellationToken: cancellationToken);

        return Ok(new
        {
            accountId = account.Id,
            account.Username,
            account.ExternalId,
            hwidHashReset = true
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var ban = await dbContext.HardwareBans.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (ban is null)
        {
            return NotFound();
        }

        var accountId = ban.AccountId;
        dbContext.HardwareBans.Remove(ban);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "ban.delete",
            actor: actor,
            entityType: "ban",
            entityId: id.ToString(),
            details: new
            {
                ban.AccountId,
                ban.HwidHash,
                ban.DeviceUserName
            },
            cancellationToken: cancellationToken);

        if (accountId.HasValue)
        {
            var now = DateTime.UtcNow;
            var hasOtherActive = await dbContext.HardwareBans.AnyAsync(
                x => x.AccountId == accountId.Value && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now),
                cancellationToken);

            if (!hasOtherActive)
            {
                var account = await dbContext.AuthAccounts.FirstOrDefaultAsync(x => x.Id == accountId.Value, cancellationToken);
                if (account is not null)
                {
                    account.Banned = false;
                    account.UpdatedAtUtc = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }

        return NoContent();
    }

    private static BanDto Map(HardwareBan ban, string? username, string? externalId)
    {
        var now = DateTime.UtcNow;
        return new BanDto(
            ban.Id,
            ban.AccountId,
            username ?? string.Empty,
            externalId ?? string.Empty,
            ban.HwidHash,
            ban.DeviceUserName,
            ban.Reason,
            ban.CreatedAtUtc,
            ban.ExpiresAtUtc,
            ban.ExpiresAtUtc == null || ban.ExpiresAtUtc > now);
    }

    private static string NormalizeReason(string reason)
    {
        var normalized = reason.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Manual ban" : normalized;
    }

    private static string NormalizeHwidHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeDeviceUserName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length > 128 ? normalized[..128] : normalized;
    }

    private async Task<ResolvedAccountContext?> ResolveAccountByUserAsync(string normalizedUser, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var account = await ResolveDirectAccountByUserAsync(normalizedUser, cancellationToken);
        var sessionSnapshot = account is not null
            ? await ResolveLatestSessionByAccountAsync(account.Id, cancellationToken)
            : await ResolveLatestSessionByUserAsync(normalizedUser, cancellationToken);

        if (account is null && sessionSnapshot is not null)
        {
            account = await dbContext.AuthAccounts.FirstOrDefaultAsync(
                x => x.Id == sessionSnapshot.AccountId,
                cancellationToken);
        }

        if (account is null)
        {
            return null;
        }

        var resolvedHwidHash = sessionSnapshot is not null
            ? NormalizeHwidHash(sessionSnapshot.HwidHash)
            : string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedHwidHash))
        {
            resolvedHwidHash = NormalizeHwidHash(account.HwidHash);
        }

        var resolvedDeviceUserName = sessionSnapshot is not null
            ? NormalizeDeviceUserName(sessionSnapshot.DeviceUserName)
            : string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedDeviceUserName))
        {
            resolvedDeviceUserName = NormalizeDeviceUserName(account.DeviceUserName);
        }

        var accountUpdated = false;
        if (!string.IsNullOrWhiteSpace(resolvedHwidHash) &&
            !string.Equals(account.HwidHash, resolvedHwidHash, StringComparison.Ordinal))
        {
            account.HwidHash = resolvedHwidHash;
            accountUpdated = true;
        }

        if (!string.IsNullOrWhiteSpace(resolvedDeviceUserName) &&
            !string.Equals(account.DeviceUserName, resolvedDeviceUserName, StringComparison.Ordinal))
        {
            account.DeviceUserName = resolvedDeviceUserName;
            accountUpdated = true;
        }

        if (accountUpdated)
        {
            account.UpdatedAtUtc = now;
        }

        return new ResolvedAccountContext(account, resolvedHwidHash, resolvedDeviceUserName);
    }

    private async Task<AuthAccount?> ResolveDirectAccountByUserAsync(string normalizedUser, CancellationToken cancellationToken)
    {
        var byExternalExact = await dbContext.AuthAccounts.FirstOrDefaultAsync(
            x => x.ExternalId == normalizedUser,
            cancellationToken);
        if (byExternalExact is not null)
        {
            return byExternalExact;
        }

        var normalizedUserLower = normalizedUser.ToLowerInvariant();
        return await dbContext.AuthAccounts
            .Where(x => x.Username.ToLower() == normalizedUserLower || x.ExternalId.ToLower() == normalizedUserLower)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<GameSessionSnapshot?> ResolveLatestSessionByAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await dbContext.ActiveGameSessions
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.LastHeartbeatAtUtc)
            .Select(x => new GameSessionSnapshot(x.AccountId, x.HwidHash, x.DeviceUserName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<GameSessionSnapshot?> ResolveLatestSessionByUserAsync(string normalizedUser, CancellationToken cancellationToken)
    {
        var normalizedUserLower = normalizedUser.ToLowerInvariant();
        return await dbContext.ActiveGameSessions
            .AsNoTracking()
            .Where(x => x.Username.ToLower() == normalizedUserLower || x.Account!.ExternalId.ToLower() == normalizedUserLower)
            .OrderByDescending(x => x.LastHeartbeatAtUtc)
            .Select(x => new GameSessionSnapshot(x.AccountId, x.HwidHash, x.DeviceUserName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record ResolvedAccountContext(AuthAccount Account, string HwidHash, string DeviceUserName);

    private sealed record GameSessionSnapshot(Guid AccountId, string HwidHash, string DeviceUserName);
}
