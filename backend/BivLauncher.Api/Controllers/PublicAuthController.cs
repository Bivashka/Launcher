using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Infrastructure;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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
    [Authorize]
    [HttpGet("session")]
    public async Task<ActionResult<PublicAuthSessionResponse>> Session(CancellationToken cancellationToken)
    {
        var externalId = User.FindFirstValue("external_id")?.Trim() ?? string.Empty;
        var username = User.Identity?.Name?.Trim() ?? string.Empty;
        var tokenSessionVersionRaw = User.FindFirstValue("session_version");

        if (string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized(new { error = "Invalid player session token." });
        }

        AuthAccount? account = null;

        if (!string.IsNullOrWhiteSpace(externalId))
        {
            account = await dbContext.AuthAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ExternalId == externalId, cancellationToken);
        }

        if (account is null && !string.IsNullOrWhiteSpace(username))
        {
            account = await dbContext.AuthAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username, cancellationToken);
        }

        if (account is null)
        {
            return Unauthorized(new { error = "Player session is not recognized." });
        }

        if (!TryParseSessionVersion(tokenSessionVersionRaw, out var tokenSessionVersion))
        {
            return Unauthorized(new { error = "Invalid player session token version." });
        }

        if (tokenSessionVersion != account.SessionVersion)
        {
            return Unauthorized(new { error = "Player session expired. Login is required." });
        }

        if (account.Banned)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Account is banned." });
        }

        var now = DateTime.UtcNow;
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

        var normalizedDeviceUserName = NormalizeDeviceUserName(account.DeviceUserName);
        if (!string.IsNullOrWhiteSpace(normalizedDeviceUserName))
        {
            var activeDeviceUserBan = await dbContext.HardwareBans
                .AsNoTracking()
                .Where(x =>
                    x.DeviceUserName == normalizedDeviceUserName &&
                    (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => x.Reason)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeDeviceUserBan is not null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = $"Device user banned: {activeDeviceUserBan}" });
            }
        }

        var roles = NormalizeRoles(account.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return Ok(new PublicAuthSessionResponse(
            Username: account.Username,
            ExternalId: account.ExternalId,
            Roles: roles));
    }

    [HttpPost("login")]
    public async Task<ActionResult<PublicAuthLoginResponse>> Login(
        [FromBody] PublicAuthLoginRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var deviceUserName = NormalizeDeviceUserName(request.DeviceUserName);
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

        if (!string.IsNullOrWhiteSpace(deviceUserName))
        {
            var activeDeviceUserBan = await dbContext.HardwareBans
                .AsNoTracking()
                .Where(x =>
                    x.DeviceUserName == deviceUserName &&
                    (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => x.Reason)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeDeviceUserBan is not null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = $"Device user banned: {activeDeviceUserBan}" });
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
        var roles = NormalizeRoles(authResult.Roles);

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
                HwidHash = hwidHash,
                DeviceUserName = deviceUserName
            };
            dbContext.AuthAccounts.Add(account);
        }
        else
        {
            account.Username = normalizedUsername;
            account.Roles = string.Join(',', roles);
            account.HwidHash = hwidHash;
            account.DeviceUserName = deviceUserName;
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

    private static string NormalizeDeviceUserName(string? rawDeviceUserName)
    {
        if (string.IsNullOrWhiteSpace(rawDeviceUserName))
        {
            return string.Empty;
        }

        var normalized = rawDeviceUserName.Trim().ToLowerInvariant();
        return normalized.Length > 128 ? normalized[..128] : normalized;
    }

    private static bool TryParseSessionVersion(string? rawValue, out int sessionVersion)
    {
        sessionVersion = 0;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        return int.TryParse(rawValue.Trim(), out sessionVersion) && sessionVersion >= 0;
    }

    private static List<string> NormalizeRoles(IEnumerable<string> roles)
    {
        var normalized = roles
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.Add("player");
        }

        return normalized;
    }
}
