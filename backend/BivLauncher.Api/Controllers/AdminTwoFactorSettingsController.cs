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
[Route("api/admin/settings/two-factor")]
public sealed class AdminTwoFactorSettingsController(
    AppDbContext dbContext,
    ITwoFactorService twoFactorService,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TwoFactorSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var stored = await dbContext.TwoFactorConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return Ok(new TwoFactorSettingsDto(
                Enabled: false,
                UpdatedAtUtc: null));
        }

        return Ok(new TwoFactorSettingsDto(
            Enabled: stored.Enabled,
            UpdatedAtUtc: stored.UpdatedAtUtc));
    }

    [HttpPut]
    public async Task<ActionResult<TwoFactorSettingsDto>> Put(
        [FromBody] TwoFactorSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.TwoFactorConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new TwoFactorConfig();
            dbContext.TwoFactorConfigs.Add(config);
        }

        config.Enabled = request.Enabled;
        config.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.two-factor.update",
            actor: actor,
            entityType: "settings",
            entityId: "two-factor",
            details: new
            {
                config.Enabled
            },
            cancellationToken: cancellationToken);

        return Ok(new TwoFactorSettingsDto(
            Enabled: config.Enabled,
            UpdatedAtUtc: config.UpdatedAtUtc));
    }

    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<TwoFactorAccountDto>>> ListAccounts(
        [FromQuery] string search = "",
        [FromQuery] bool requiredOnly = false,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedSearch = search.Trim();
        var take = Math.Clamp(limit, 1, 500);

        var query = dbContext.AuthAccounts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Username, $"%{normalizedSearch}%") ||
                EF.Functions.ILike(x.ExternalId, $"%{normalizedSearch}%"));
        }

        if (requiredOnly)
        {
            query = query.Where(x => x.TwoFactorRequired);
        }

        var accounts = await query
            .OrderByDescending(x => x.TwoFactorRequired)
            .ThenBy(x => x.Username)
            .Take(take)
            .Select(x => new TwoFactorAccountDto(
                x.Id,
                x.Username,
                x.ExternalId,
                x.TwoFactorRequired,
                !string.IsNullOrWhiteSpace(x.TwoFactorSecret),
                x.TwoFactorEnrolledAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(accounts);
    }

    [HttpPut("accounts/{id:guid}")]
    public async Task<ActionResult<TwoFactorAccountDto>> PutAccount(
        Guid id,
        [FromBody] TwoFactorAccountUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.AuthAccounts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound(new { error = "Account not found." });
        }

        account.TwoFactorRequired = request.TwoFactorRequired;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "two-factor.account.requirement.update",
            actor: actor,
            entityType: "account",
            entityId: account.ExternalId,
            details: new
            {
                account.Id,
                account.Username,
                account.TwoFactorRequired
            },
            cancellationToken: cancellationToken);

        return Ok(MapAccount(account));
    }

    [HttpPost("accounts/{id:guid}/enroll")]
    public async Task<ActionResult<TwoFactorEnrollResponse>> Enroll(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.AuthAccounts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound(new { error = "Account not found." });
        }

        var secret = twoFactorService.GenerateSecret();
        account.TwoFactorSecret = secret;
        account.TwoFactorEnrolledAtUtc = null;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "two-factor.account.enroll",
            actor: actor,
            entityType: "account",
            entityId: account.ExternalId,
            details: new
            {
                account.Id,
                account.Username,
                hasSecret = true
            },
            cancellationToken: cancellationToken);

        var uri = twoFactorService.BuildOtpAuthUri("BivLauncher", account.Username, secret);
        return Ok(new TwoFactorEnrollResponse(
            Account: MapAccount(account),
            Secret: secret,
            OtpAuthUri: uri));
    }

    [HttpPost("accounts/{id:guid}/reset")]
    public async Task<ActionResult<TwoFactorAccountDto>> Reset(
        Guid id,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.AuthAccounts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound(new { error = "Account not found." });
        }

        account.TwoFactorSecret = string.Empty;
        account.TwoFactorEnrolledAtUtc = null;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "two-factor.account.reset",
            actor: actor,
            entityType: "account",
            entityId: account.ExternalId,
            details: new
            {
                account.Id,
                account.Username
            },
            cancellationToken: cancellationToken);

        return Ok(MapAccount(account));
    }

    private static TwoFactorAccountDto MapAccount(AuthAccount account)
    {
        return new TwoFactorAccountDto(
            account.Id,
            account.Username,
            account.ExternalId,
            account.TwoFactorRequired,
            !string.IsNullOrWhiteSpace(account.TwoFactorSecret),
            account.TwoFactorEnrolledAtUtc,
            account.UpdatedAtUtc);
    }
}
