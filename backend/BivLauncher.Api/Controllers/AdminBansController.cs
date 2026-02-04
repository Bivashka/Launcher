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
            Reason = NormalizeReason(request.Reason),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = request.ExpiresAtUtc
        };

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

        var account = await dbContext.AuthAccounts.FirstOrDefaultAsync(
            x => x.Username == normalizedUser || x.ExternalId == normalizedUser,
            cancellationToken);
        if (account is null)
        {
            return NotFound(new { error = "Account not found." });
        }

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
            HwidHash = string.Empty,
            Reason = NormalizeReason(request.Reason),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = request.ExpiresAtUtc
        };

        if (request.ExpiresAtUtc is null)
        {
            account.Banned = true;
        }
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

        var account = await dbContext.AuthAccounts.FirstOrDefaultAsync(
            x => x.Username == normalizedUser || x.ExternalId == normalizedUser,
            cancellationToken);
        if (account is null)
        {
            return NotFound(new { error = "Account not found." });
        }

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
                ban.HwidHash
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
}
