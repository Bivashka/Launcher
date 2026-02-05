using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Infrastructure;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[EnableRateLimiting(RateLimitPolicies.PublicLoginPolicy)]
[Route("api/public/auth")]
public sealed class PublicAuthController(
    AppDbContext dbContext,
    IExternalAuthService externalAuthService,
    IHardwareFingerprintService hardwareFingerprintService,
    IJwtTokenService jwtTokenService,
    ITwoFactorService twoFactorService) : ControllerBase
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

        var isTwoFactorEnabled = await dbContext.TwoFactorConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => x.Enabled)
            .FirstOrDefaultAsync(cancellationToken);
        var requiresTwoFactor = isTwoFactorEnabled && account.TwoFactorRequired;
        if (requiresTwoFactor)
        {
            var twoFactorResponse = await ValidateTwoFactorAsync(account, request.TwoFactorCode, cancellationToken);
            if (twoFactorResponse is not null)
            {
                return Ok(twoFactorResponse);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var token = jwtTokenService.CreatePlayerToken(account, roles);

        return Ok(new PublicAuthLoginResponse(
            Token: token,
            TokenType: "Bearer",
            Username: account.Username,
            ExternalId: account.ExternalId,
            Roles: roles,
            RequiresTwoFactor: false,
            TwoFactorEnrolled: true));
    }

    private async Task<PublicAuthLoginResponse?> ValidateTwoFactorAsync(
        AuthAccount account,
        string? rawCode,
        CancellationToken cancellationToken)
    {
        var hasSecret = !string.IsNullOrWhiteSpace(account.TwoFactorSecret);
        if (!hasSecret)
        {
            account.TwoFactorSecret = twoFactorService.GenerateSecret();
            account.TwoFactorEnrolledAtUtc = null;
            account.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var normalizedCode = NormalizeTwoFactorCode(rawCode);
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            if (dbContext.ChangeTracker.HasChanges())
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return BuildTwoFactorChallenge(account, "Two-factor code is required.");
        }

        var codeValid = twoFactorService.ValidateCode(account.TwoFactorSecret, normalizedCode, DateTime.UtcNow);
        if (!codeValid)
        {
            if (dbContext.ChangeTracker.HasChanges())
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return BuildTwoFactorChallenge(account, "Invalid two-factor code.");
        }

        if (!account.TwoFactorEnrolledAtUtc.HasValue)
        {
            account.TwoFactorEnrolledAtUtc = DateTime.UtcNow;
            account.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    private PublicAuthLoginResponse BuildTwoFactorChallenge(AuthAccount account, string message)
    {
        var hasSecret = !string.IsNullOrWhiteSpace(account.TwoFactorSecret);
        var enrolled = hasSecret && account.TwoFactorEnrolledAtUtc.HasValue;

        var secret = enrolled ? string.Empty : account.TwoFactorSecret;
        var provisioningUri = enrolled || string.IsNullOrWhiteSpace(secret)
            ? string.Empty
            : twoFactorService.BuildOtpAuthUri("BivLauncher", account.Username, secret);

        return new PublicAuthLoginResponse(
            Token: string.Empty,
            TokenType: "Bearer",
            Username: account.Username,
            ExternalId: account.ExternalId,
            Roles: [],
            RequiresTwoFactor: true,
            TwoFactorEnrolled: enrolled,
            TwoFactorProvisioningUri: provisioningUri,
            TwoFactorSecret: secret,
            Message: message);
    }

    private static string NormalizeTwoFactorCode(string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return string.Empty;
        }

        return new string(rawCode.Where(char.IsDigit).ToArray());
    }
}
