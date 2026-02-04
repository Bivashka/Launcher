using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Route("api/public/auth")]
public sealed class PublicAuthController(
    AppDbContext dbContext,
    IExternalAuthService externalAuthService,
    IHardwareFingerprintService hardwareFingerprintService,
    IJwtTokenService jwtTokenService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<PublicAuthLoginResponse>> Login(
        [FromBody] PublicAuthLoginRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var hwidHash = hardwareFingerprintService.NormalizeLegacyHash(request.HwidHash);
        var hwidFingerprint = hardwareFingerprintService.NormalizeLegacyHash(request.HwidFingerprint);
        if (!string.IsNullOrWhiteSpace(hwidFingerprint))
        {
            if (!hardwareFingerprintService.TryComputeHwidHash(hwidFingerprint, out hwidHash, out var hwidError))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = hwidError });
            }
        }

        var now = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(hwidHash))
        {
            var activeHardwareBan = await dbContext.HardwareBans
                .AsNoTracking()
                .Where(x =>
                    x.HwidHash == hwidHash &&
                    (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => x.Reason)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeHardwareBan is not null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = $"Hardware banned: {activeHardwareBan}" });
            }
        }

        var authResult = await externalAuthService.AuthenticateAsync(
            username,
            request.Password,
            hwidHash,
            cancellationToken);

        if (!authResult.Success)
        {
            return Unauthorized(new { error = authResult.ErrorMessage });
        }

        if (authResult.Banned)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Account is banned." });
        }

        var normalizedExternalId = string.IsNullOrWhiteSpace(authResult.ExternalId)
            ? username
            : authResult.ExternalId.Trim();
        var normalizedUsername = string.IsNullOrWhiteSpace(authResult.Username)
            ? username
            : authResult.Username.Trim();
        var roles = authResult.Roles
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roles.Count == 0)
        {
            roles.Add("player");
        }

        var account = await dbContext.AuthAccounts.FirstOrDefaultAsync(
            x => x.ExternalId == normalizedExternalId,
            cancellationToken);

        if (account is null)
        {
            account = new AuthAccount
            {
                ExternalId = normalizedExternalId,
                Username = normalizedUsername,
                Roles = string.Join(',', roles),
                Banned = false,
                HwidHash = hwidHash
            };
            dbContext.AuthAccounts.Add(account);
        }
        else
        {
            account.Username = normalizedUsername;
            account.Roles = string.Join(',', roles);
            account.HwidHash = hwidHash;
            account.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (account.Banned)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Account is banned." });
        }

        var activeAccountBan = await dbContext.HardwareBans
            .AsNoTracking()
            .Where(x =>
                x.AccountId == account.Id &&
                (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.Reason)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeAccountBan is not null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = $"Account is banned: {activeAccountBan}" });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var token = jwtTokenService.CreatePlayerToken(account, roles);

        return Ok(new PublicAuthLoginResponse(
            Token: token,
            TokenType: "Bearer",
            Username: account.Username,
            ExternalId: account.ExternalId,
            Roles: roles));
    }
}
