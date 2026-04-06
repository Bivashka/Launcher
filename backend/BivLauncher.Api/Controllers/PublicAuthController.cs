using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Infrastructure;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BivLauncher.Api.Controllers;

[ApiController]
[EnableRateLimiting(RateLimitPolicies.PublicLoginPolicy)]
[Route("api/public/auth")]
public sealed class PublicAuthController(
    AppDbContext dbContext,
    IConfiguration configuration,
    IExternalAuthService externalAuthService,
    IHardwareFingerprintService hardwareFingerprintService,
    IJwtTokenService jwtTokenService,
    ITwoFactorService twoFactorService,
    ISecuritySettingsProvider securitySettingsProvider,
    IAdminAuditService auditService,
    ILogger<PublicAuthController> logger) : ControllerBase
{
    private const string LauncherVerifiedClaimType = "launcher_verified";
    private const string LauncherVerifiedClaimValue = "1";
    private const string LauncherVersionClaimType = "launcher_version";
    private const string LauncherProofIdClaimType = "launcher_proof_id";
    private const string LauncherClientHeaderName = "X-BivLauncher-Client";
    private const string LauncherClientHeaderPrefix = "BivLauncher.Client/";
    private const string LauncherProofHeaderName = "X-BivLauncher-Proof";
    private const string LauncherMinClientVersionConfigKey = "LAUNCHER_MIN_CLIENT_VERSION";
    private static readonly ConcurrentDictionary<string, PendingTwoFactorChallenge> PendingTwoFactorChallenges = new(StringComparer.Ordinal);
    private static readonly TimeSpan PendingTwoFactorChallengeLifetime = TimeSpan.FromMinutes(3);

    [Authorize]
    [HttpGet("session")]
    public async Task<ActionResult<PublicAuthSessionResponse>> Session(CancellationToken cancellationToken)
    {
        if (!IsLauncherClientAllowed(out var launcherError))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = launcherError });
        }

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

        if (IsLauncherProofEnforced() &&
            (!HasLauncherVerifiedClaim(User) || !IsTokenLauncherProofAllowed(User)))
        {
            return Unauthorized(new { error = "Player session expired. Login is required." });
        }

        if (IsLauncherMinVersionEnforced() && !IsTokenLauncherVersionAllowed(User))
        {
            return Unauthorized(new { error = "Player session expired. Login is required." });
        }

        if (account.Banned)
        {
            return BanResponse("account_ban", "Account is banned.");
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
            return BanResponse("account_ban", $"Account is banned: {activeAccountBan}");
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
                return BanResponse("device_user_ban", $"Device user banned: {activeDeviceUserBan}");
            }
        }

        var roles = await ApplyConfiguredSecurityRolesAsync(
            account.Username,
            account.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            cancellationToken);

        return Ok(new PublicAuthSessionResponse(
            Username: account.Username,
            ExternalId: account.ExternalId,
            Roles: roles));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!IsLauncherClientAllowed(out var launcherError))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = launcherError });
        }

        var externalId = User.FindFirstValue("external_id")?.Trim() ?? string.Empty;
        var username = User.Identity?.Name?.Trim() ?? string.Empty;
        var tokenSessionVersionRaw = User.FindFirstValue("session_version");

        if (string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized(new { error = "Invalid player session token." });
        }

        var account = await ResolveAccountForSessionMutationAsync(externalId, username, cancellationToken);
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

        if (IsLauncherProofEnforced() &&
            (!HasLauncherVerifiedClaim(User) || !IsTokenLauncherProofAllowed(User)))
        {
            return Unauthorized(new { error = "Player session expired. Login is required." });
        }

        if (IsLauncherMinVersionEnforced() && !IsTokenLauncherVersionAllowed(User))
        {
            return Unauthorized(new { error = "Player session expired. Login is required." });
        }

        account.SessionVersion++;
        account.UpdatedAtUtc = DateTime.UtcNow;
        var activeGameSessions = await dbContext.ActiveGameSessions
            .Where(x => x.AccountId == account.Id)
            .ToListAsync(cancellationToken);
        if (activeGameSessions.Count > 0)
        {
            dbContext.ActiveGameSessions.RemoveRange(activeGameSessions);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [Authorize]
    [HttpPost("game-session/start")]
    public async Task<ActionResult<PublicGameSessionStartResponse>> StartGameSession(
        [FromBody] PublicGameSessionStartRequest request,
        CancellationToken cancellationToken)
    {
        var (account, errorResult) = await ResolveAuthorizedAccountForCurrentRequestAsync(cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var securitySettings = securitySettingsProvider.GetCachedSettings();
        var now = DateTime.UtcNow;
        await PruneExpiredGameSessionsAsync(now, cancellationToken);

        var normalizedHwidHash = NormalizeHwidHash(account!.HwidHash);
        var normalizedDeviceUserName = NormalizeDeviceUserName(account.DeviceUserName);
        var hasDeviceIdentity = !string.IsNullOrWhiteSpace(normalizedHwidHash) || !string.IsNullOrWhiteSpace(normalizedDeviceUserName);

        var activeAccountsOnDevice = 1;
        if (hasDeviceIdentity)
        {
            var deviceSessionsQuery = BuildDeviceSessionsQuery(normalizedHwidHash, normalizedDeviceUserName, now);
            var otherActiveAccountsOnDevice = await deviceSessionsQuery
                .Where(x => x.AccountId != account.Id)
                .Select(x => x.AccountId)
                .Distinct()
                .CountAsync(cancellationToken);
            activeAccountsOnDevice += otherActiveAccountsOnDevice;

            if (securitySettings.MaxConcurrentGameAccountsPerDevice > 0 &&
                activeAccountsOnDevice > securitySettings.MaxConcurrentGameAccountsPerDevice)
            {
                return Conflict(new
                {
                    error = $"Device account limit reached. Active accounts on this computer: {activeAccountsOnDevice - 1}. Allowed: {securitySettings.MaxConcurrentGameAccountsPerDevice}.",
                    code = "device_account_limit"
                });
            }
        }

        var expiresAtUtc = now.AddSeconds(securitySettings.GameSessionExpirationSeconds);
        var normalizedServerName = string.IsNullOrWhiteSpace(request.ServerName)
            ? string.Empty
            : request.ServerName.Trim();
        var session = await dbContext.ActiveGameSessions.FirstOrDefaultAsync(
            x => x.AccountId == account.Id,
            cancellationToken);
        if (session is null)
        {
            session = new ActiveGameSession
            {
                AccountId = account.Id,
                Username = account.Username,
                HwidHash = normalizedHwidHash,
                DeviceUserName = normalizedDeviceUserName,
                ServerId = request.ServerId,
                ServerName = normalizedServerName,
                StartedAtUtc = now,
                LastHeartbeatAtUtc = now,
                ExpiresAtUtc = expiresAtUtc
            };
            dbContext.ActiveGameSessions.Add(session);
        }
        else
        {
            session.Username = account.Username;
            session.HwidHash = normalizedHwidHash;
            session.DeviceUserName = normalizedDeviceUserName;
            session.ServerId = request.ServerId;
            session.ServerName = normalizedServerName;
            session.LastHeartbeatAtUtc = now;
            session.ExpiresAtUtc = expiresAtUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new PublicGameSessionStartResponse(
            SessionId: session.Id,
            HeartbeatIntervalSeconds: securitySettings.GameSessionHeartbeatIntervalSeconds,
            ExpiresAfterSeconds: securitySettings.GameSessionExpirationSeconds,
            ActiveAccountsOnDevice: activeAccountsOnDevice,
            Limit: securitySettings.MaxConcurrentGameAccountsPerDevice));
    }

    [Authorize]
    [HttpPost("game-session/heartbeat")]
    public async Task<IActionResult> HeartbeatGameSession(
        [FromBody] PublicGameSessionHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        var (account, errorResult) = await ResolveAuthorizedAccountForCurrentRequestAsync(cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        if (request.SessionId == Guid.Empty)
        {
            return BadRequest(new { error = "SessionId is required." });
        }

        var now = DateTime.UtcNow;
        await PruneExpiredGameSessionsAsync(now, cancellationToken);

        var session = await dbContext.ActiveGameSessions.FirstOrDefaultAsync(
            x => x.Id == request.SessionId && x.AccountId == account!.Id,
            cancellationToken);
        if (session is null)
        {
            return NotFound(new { error = "Game session not found." });
        }

        var securitySettings = securitySettingsProvider.GetCachedSettings();
        session.LastHeartbeatAtUtc = now;
        session.ExpiresAtUtc = now.AddSeconds(securitySettings.GameSessionExpirationSeconds);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [Authorize]
    [HttpPost("game-session/stop")]
    public async Task<IActionResult> StopGameSession(
        [FromBody] PublicGameSessionStopRequest request,
        CancellationToken cancellationToken)
    {
        var (account, errorResult) = await ResolveAuthorizedAccountForCurrentRequestAsync(cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        if (request.SessionId == Guid.Empty)
        {
            return BadRequest(new { error = "SessionId is required." });
        }

        var session = await dbContext.ActiveGameSessions.FirstOrDefaultAsync(
            x => x.Id == request.SessionId && x.AccountId == account!.Id,
            cancellationToken);
        if (session is null)
        {
            return NoContent();
        }

        dbContext.ActiveGameSessions.Remove(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [Authorize]
    [HttpPost("security-violation")]
    public async Task<ActionResult<PublicSecurityViolationReportResponse>> ReportSecurityViolation(
        [FromBody] PublicSecurityViolationReportRequest request,
        CancellationToken cancellationToken)
    {
        var (account, errorResult) = await ResolveAuthorizedAccountForCurrentRequestAsync(cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        if (User.IsInRole("admin") || User.IsInRole("administrator"))
        {
            return Ok(new PublicSecurityViolationReportResponse(
                Banned: false,
                Exempt: true,
                ExpiresAtUtc: null,
                Reason: "Administrative accounts are exempt from launcher anti-tamper auto-ban."));
        }

        var normalizedReason = NormalizeSecurityViolationReason(request.Reason);
        var normalizedEvidence = NormalizeSecurityViolationEvidence(request.Evidence);
        var normalizedDeviceUserName = NormalizeDeviceUserName(
            string.IsNullOrWhiteSpace(request.DeviceUserName)
                ? account!.DeviceUserName
                : request.DeviceUserName);
        var normalizedHwidHash = NormalizeHwidHash(account!.HwidHash);
        var hwidFingerprint = hardwareFingerprintService.NormalizeLegacyHash(request.HwidFingerprint);
        if (!string.IsNullOrWhiteSpace(hwidFingerprint))
        {
            if (!hardwareFingerprintService.TryComputeHwidHash(hwidFingerprint, out normalizedHwidHash, out var hwidError))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = hwidError });
            }

            normalizedHwidHash = NormalizeHwidHash(normalizedHwidHash);
        }

        if (string.IsNullOrWhiteSpace(normalizedHwidHash) && string.IsNullOrWhiteSpace(normalizedDeviceUserName))
        {
            return BadRequest(new { error = "Device identity is required for security violation ban." });
        }

        var now = DateTime.UtcNow;
        var activeBan = await dbContext.HardwareBans
            .AsNoTracking()
            .Where(x =>
                (x.AccountId == account.Id ||
                 (!string.IsNullOrWhiteSpace(normalizedHwidHash) && x.HwidHash == normalizedHwidHash) ||
                 (!string.IsNullOrWhiteSpace(normalizedDeviceUserName) && x.DeviceUserName == normalizedDeviceUserName)) &&
                (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeBan is not null)
        {
            return Ok(new PublicSecurityViolationReportResponse(
                Banned: true,
                Exempt: false,
                ExpiresAtUtc: activeBan.ExpiresAtUtc,
                Reason: activeBan.Reason));
        }

        var banReason = $"Launcher anti-tamper: {normalizedReason}";
        var expiresAtUtc = now.AddDays(30);

        account.SessionVersion++;
        account.UpdatedAtUtc = now;
        if (!string.IsNullOrWhiteSpace(normalizedHwidHash))
        {
            account.HwidHash = normalizedHwidHash;
        }

        if (!string.IsNullOrWhiteSpace(normalizedDeviceUserName))
        {
            account.DeviceUserName = normalizedDeviceUserName;
        }

        var ban = new HardwareBan
        {
            AccountId = account.Id,
            HwidHash = normalizedHwidHash,
            DeviceUserName = normalizedDeviceUserName,
            Reason = banReason,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };

        dbContext.HardwareBans.Add(ban);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            action: "security.violation.report",
            actor: account.Username,
            entityType: "ban",
            entityId: ban.Id.ToString(),
            details: new
            {
                accountId = account.Id,
                account.Username,
                account.ExternalId,
                account.SessionVersion,
                hwidHash = normalizedHwidHash,
                deviceUserName = normalizedDeviceUserName,
                normalizedReason,
                normalizedEvidence,
                expiresAtUtc
            },
            cancellationToken: cancellationToken);

        return Ok(new PublicSecurityViolationReportResponse(
            Banned: true,
            Exempt: false,
            ExpiresAtUtc: expiresAtUtc,
            Reason: banReason));
    }

    [HttpPost("login")]
    public async Task<ActionResult<PublicAuthLoginResponse>> Login(
        [FromBody] PublicAuthLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsLauncherClientAllowed(out var launcherError, out var launcherClientVersionRaw))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = launcherError });
        }

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
        var normalizedTwoFactorCode = NormalizeTwoFactorCode(request.TwoFactorCode);
        var pendingTwoFactorChallengeKey = BuildPendingTwoFactorChallengeKey(username, hwidHash, deviceUserName);

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
                return BanResponse("hardware_ban", $"Hardware banned: {activeHardwareBan}");
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
                return BanResponse("device_user_ban", $"Device user banned: {activeDeviceUserBan}");
            }
        }

        AuthAccount? account = null;
        List<string> roles = [];
        if (!string.IsNullOrWhiteSpace(normalizedTwoFactorCode) &&
            TryGetPendingTwoFactorChallenge(pendingTwoFactorChallengeKey, out var pendingTwoFactorChallenge))
        {
            account = await dbContext.AuthAccounts.FirstOrDefaultAsync(
                x => x.Id == pendingTwoFactorChallenge.AccountId,
                cancellationToken);
            if (account is null)
            {
                RemovePendingTwoFactorChallenge(pendingTwoFactorChallengeKey);
            }
            else
            {
                roles = NormalizeRoles(pendingTwoFactorChallenge.Roles);
                account.HwidHash = hwidHash;
                account.DeviceUserName = deviceUserName;
                account.UpdatedAtUtc = now;
            }
        }

        if (account is null)
        {
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
                return BanResponse("account_ban", "Account is banned.");
            }

            var normalizedExternalId = string.IsNullOrWhiteSpace(authResult.ExternalId)
                ? username
                : authResult.ExternalId.Trim();
            var normalizedUsername = string.IsNullOrWhiteSpace(authResult.Username)
                ? username
                : authResult.Username.Trim();
            if (string.IsNullOrWhiteSpace(normalizedExternalId) || string.IsNullOrWhiteSpace(normalizedUsername))
            {
                return Unauthorized(new { error = "Auth provider returned invalid identity payload." });
            }

            var providerRoles = NormalizeRoles(authResult.Roles);
            roles = await ApplyConfiguredSecurityRolesAsync(
                normalizedUsername,
                providerRoles,
                cancellationToken);

            var accountByExternalId = await dbContext.AuthAccounts.FirstOrDefaultAsync(
                x => x.ExternalId == normalizedExternalId,
                cancellationToken);
            var accountByUsername = await dbContext.AuthAccounts.FirstOrDefaultAsync(
                x => x.Username == normalizedUsername,
                cancellationToken);
            var canRelinkLegacyUsernameAccount =
                accountByExternalId is null &&
                accountByUsername is not null &&
                IsLegacyUsernameExternalId(accountByUsername);

            if (accountByExternalId is not null &&
                !string.Equals(
                    accountByExternalId.Username,
                    normalizedUsername,
                    StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Auth identity mismatch by externalId. ExternalId={ExternalId}, existingUsername={ExistingUsername}, providerUsername={ProviderUsername}",
                    normalizedExternalId,
                    accountByExternalId.Username,
                    normalizedUsername);
                return Conflict(new
                {
                    error = "Auth provider identity mismatch: externalId is already linked to another username."
                });
            }

            if (accountByUsername is not null &&
                !string.Equals(
                    accountByUsername.ExternalId,
                    normalizedExternalId,
                    StringComparison.OrdinalIgnoreCase) &&
                !canRelinkLegacyUsernameAccount)
            {
                logger.LogWarning(
                    "Auth identity mismatch by username. Username={Username}, existingExternalId={ExistingExternalId}, providerExternalId={ProviderExternalId}",
                    normalizedUsername,
                    accountByUsername.ExternalId,
                    normalizedExternalId);
                return Conflict(new
                {
                    error = "Auth provider identity mismatch: username is already linked to another externalId."
                });
            }

            account = accountByExternalId ?? accountByUsername;

            if (account is null)
            {
                account = new AuthAccount
                {
                    ExternalId = normalizedExternalId,
                    Username = normalizedUsername,
                    Roles = string.Join(',', providerRoles),
                    Banned = false,
                    HwidHash = hwidHash,
                    DeviceUserName = deviceUserName
                };
                dbContext.AuthAccounts.Add(account);
            }
            else
            {
                if (canRelinkLegacyUsernameAccount)
                {
                    logger.LogInformation(
                        "Auth legacy username-based externalId relinked. Username={Username}, oldExternalId={OldExternalId}, newExternalId={NewExternalId}",
                        account.Username,
                        account.ExternalId,
                        normalizedExternalId);
                }

                account.Username = normalizedUsername;
                account.ExternalId = normalizedExternalId;
                account.Roles = string.Join(',', providerRoles);
                account.HwidHash = hwidHash;
                account.DeviceUserName = deviceUserName;
                account.UpdatedAtUtc = now;
            }
        }

        if (account.Banned)
        {
            return BanResponse("account_ban", "Account is banned.");
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
            return BanResponse("account_ban", $"Account is banned: {activeAccountBan}");
        }

        var isTwoFactorEnabled = await dbContext.TwoFactorConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => x.Enabled)
            .FirstOrDefaultAsync(cancellationToken);
        var requiresTwoFactor = isTwoFactorEnabled && account.TwoFactorRequired;
        if (requiresTwoFactor)
        {
            var twoFactorResponse = await ValidateTwoFactorAsync(account, normalizedTwoFactorCode, cancellationToken);
            if (twoFactorResponse is not null)
            {
                SetPendingTwoFactorChallenge(pendingTwoFactorChallengeKey, account.Id, roles);
                return Ok(twoFactorResponse);
            }
        }

        RemovePendingTwoFactorChallenge(pendingTwoFactorChallengeKey);
        await dbContext.SaveChangesAsync(cancellationToken);

        var launcherProofId = ResolveLauncherProofId();
        var token = jwtTokenService.CreatePlayerToken(
            account,
            roles,
            launcherClientVersionRaw,
            launcherProofId);

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

    private ObjectResult BanResponse(string code, string error)
    {
        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            error,
            code
        });
    }

    private bool IsLauncherClientAllowed(out string error)
    {
        return IsLauncherClientAllowed(out error, out _);
    }

    private bool IsLauncherClientAllowed(out string error, out string launcherClientVersionRaw)
    {
        error = string.Empty;
        launcherClientVersionRaw = string.Empty;
        if (!Request.Headers.TryGetValue(LauncherClientHeaderName, out var launcherClientHeaderValues))
        {
            error = "Launcher client verification failed (missing client header).";
            return false;
        }

        var launcherClientHeader = launcherClientHeaderValues.ToString().Trim();
        if (!LauncherVersionParser.TryExtractClientVersion(
                launcherClientHeader,
                LauncherClientHeaderPrefix,
                out launcherClientVersionRaw,
                out var launcherClientVersion))
        {
            error = "Launcher client verification failed (invalid client header).";
            return false;
        }

        var minimumLauncherVersionRaw = (configuration[LauncherMinClientVersionConfigKey] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(minimumLauncherVersionRaw))
        {
            if (!LauncherVersionParser.TryParseComparableVersion(minimumLauncherVersionRaw, out var minimumLauncherVersion))
            {
                logger.LogError(
                    "Invalid launcher min client version configured in {ConfigKey}: '{ConfiguredValue}'.",
                    LauncherMinClientVersionConfigKey,
                    minimumLauncherVersionRaw);
                error = "Launcher client verification failed (invalid minimum launcher version config).";
                return false;
            }

            if (launcherClientVersion < minimumLauncherVersion)
            {
                error = $"Launcher version '{launcherClientVersionRaw}' is not supported. Minimum required version: '{minimumLauncherVersionRaw}'.";
                return false;
            }
        }

        var requiredProof = (configuration["LAUNCHER_CLIENT_PROOF"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requiredProof))
        {
            return true;
        }

        if (!Request.Headers.TryGetValue(LauncherProofHeaderName, out var launcherProofValues))
        {
            error = "Launcher client verification failed (missing proof header).";
            return false;
        }

        var launcherProof = launcherProofValues.ToString().Trim();
        if (!string.Equals(launcherProof, requiredProof, StringComparison.Ordinal))
        {
            error = "Launcher client verification failed (proof mismatch).";
            return false;
        }

        return true;
    }

    private bool IsLauncherProofEnforced()
    {
        var requiredProof = (configuration["LAUNCHER_CLIENT_PROOF"] ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(requiredProof);
    }

    private static bool HasLauncherVerifiedClaim(ClaimsPrincipal principal)
    {
        var claimValue = principal.FindFirstValue(LauncherVerifiedClaimType)?.Trim() ?? string.Empty;
        return string.Equals(claimValue, LauncherVerifiedClaimValue, StringComparison.Ordinal);
    }

    private bool IsTokenLauncherProofAllowed(ClaimsPrincipal principal)
    {
        var expectedProofId = ResolveLauncherProofId();
        if (string.IsNullOrWhiteSpace(expectedProofId))
        {
            return false;
        }

        var tokenProofId = principal.FindFirstValue(LauncherProofIdClaimType)?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(tokenProofId) &&
               string.Equals(tokenProofId, expectedProofId, StringComparison.Ordinal);
    }

    private bool IsLauncherMinVersionEnforced()
    {
        var minimumLauncherVersionRaw = (configuration[LauncherMinClientVersionConfigKey] ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(minimumLauncherVersionRaw);
    }

    private string ResolveLauncherProofId()
    {
        var requiredProof = (configuration["LAUNCHER_CLIENT_PROOF"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requiredProof))
        {
            return string.Empty;
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(requiredProof));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private bool IsTokenLauncherVersionAllowed(ClaimsPrincipal principal)
    {
        var minimumLauncherVersionRaw = (configuration[LauncherMinClientVersionConfigKey] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(minimumLauncherVersionRaw))
        {
            return true;
        }

        if (!LauncherVersionParser.TryParseComparableVersion(minimumLauncherVersionRaw, out var minimumLauncherVersion))
        {
            logger.LogError(
                "Invalid launcher min client version configured in {ConfigKey}: '{ConfiguredValue}'.",
                LauncherMinClientVersionConfigKey,
                minimumLauncherVersionRaw);
            return false;
        }

        var tokenLauncherVersionRaw = principal.FindFirstValue(LauncherVersionClaimType)?.Trim() ?? string.Empty;
        if (!LauncherVersionParser.TryParseComparableVersion(tokenLauncherVersionRaw, out var tokenLauncherVersion))
        {
            return false;
        }

        return tokenLauncherVersion >= minimumLauncherVersion;
    }

    private static string BuildPendingTwoFactorChallengeKey(string username, string hwidHash, string deviceUserName)
    {
        var normalizedUsername = (username ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedHwid = (hwidHash ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedDeviceUser = (deviceUserName ?? string.Empty).Trim().ToLowerInvariant();
        return $"{normalizedUsername}|{normalizedHwid}|{normalizedDeviceUser}";
    }

    private async Task<AuthAccount?> ResolveAccountForSessionMutationAsync(
        string externalId,
        string username,
        CancellationToken cancellationToken)
    {
        AuthAccount? account = null;
        if (!string.IsNullOrWhiteSpace(externalId))
        {
            account = await dbContext.AuthAccounts
                .FirstOrDefaultAsync(x => x.ExternalId == externalId, cancellationToken);
        }

        if (account is null && !string.IsNullOrWhiteSpace(username))
        {
            account = await dbContext.AuthAccounts
                .FirstOrDefaultAsync(x => x.Username == username, cancellationToken);
        }

        return account;
    }

    private async Task<(AuthAccount? Account, ActionResult? ErrorResult)> ResolveAuthorizedAccountForCurrentRequestAsync(
        CancellationToken cancellationToken)
    {
        if (!IsLauncherClientAllowed(out var launcherError))
        {
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { error = launcherError }));
        }

        var externalId = User.FindFirstValue("external_id")?.Trim() ?? string.Empty;
        var username = User.Identity?.Name?.Trim() ?? string.Empty;
        var tokenSessionVersionRaw = User.FindFirstValue("session_version");
        if (string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(username))
        {
            return (null, Unauthorized(new { error = "Invalid player session token." }));
        }

        var account = await ResolveAccountForSessionMutationAsync(externalId, username, cancellationToken);
        if (account is null)
        {
            return (null, Unauthorized(new { error = "Player session is not recognized." }));
        }

        if (!TryParseSessionVersion(tokenSessionVersionRaw, out var tokenSessionVersion))
        {
            return (null, Unauthorized(new { error = "Invalid player session token version." }));
        }

        if (tokenSessionVersion != account.SessionVersion)
        {
            return (null, Unauthorized(new { error = "Player session expired. Login is required." }));
        }

        if (IsLauncherProofEnforced() &&
            (!HasLauncherVerifiedClaim(User) || !IsTokenLauncherProofAllowed(User)))
        {
            return (null, Unauthorized(new { error = "Player session expired. Login is required." }));
        }

        if (IsLauncherMinVersionEnforced() && !IsTokenLauncherVersionAllowed(User))
        {
            return (null, Unauthorized(new { error = "Player session expired. Login is required." }));
        }

        if (account.Banned)
        {
            return (null, BanResponse("account_ban", "Account is banned."));
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
            return (null, BanResponse("account_ban", $"Account is banned: {activeAccountBan}"));
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
                return (null, BanResponse("device_user_ban", $"Device user banned: {activeDeviceUserBan}"));
            }
        }

        return (account, null);
    }

    private IQueryable<ActiveGameSession> BuildDeviceSessionsQuery(
        string normalizedHwidHash,
        string normalizedDeviceUserName,
        DateTime nowUtc)
    {
        var query = dbContext.ActiveGameSessions
            .Where(x => x.ExpiresAtUtc > nowUtc)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedHwidHash))
        {
            return query.Where(x => x.HwidHash == normalizedHwidHash);
        }

        if (!string.IsNullOrWhiteSpace(normalizedDeviceUserName))
        {
            return query.Where(x => x.DeviceUserName == normalizedDeviceUserName);
        }

        return query.Where(_ => false);
    }

    private async Task PruneExpiredGameSessionsAsync(DateTime nowUtc, CancellationToken cancellationToken)
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

    private static bool TryGetPendingTwoFactorChallenge(string key, out PendingTwoFactorChallenge challenge)
    {
        challenge = default!;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!PendingTwoFactorChallenges.TryGetValue(key, out var stored))
        {
            return false;
        }

        if (stored.ExpiresAtUtc <= DateTime.UtcNow)
        {
            PendingTwoFactorChallenges.TryRemove(key, out _);
            return false;
        }

        challenge = stored;
        return true;
    }

    private static void SetPendingTwoFactorChallenge(string key, Guid accountId, IEnumerable<string> roles)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var normalizedRoles = NormalizeRoles(roles);
        PendingTwoFactorChallenges[key] = new PendingTwoFactorChallenge(
            accountId,
            normalizedRoles,
            DateTime.UtcNow.Add(PendingTwoFactorChallengeLifetime));
    }

    private static void RemovePendingTwoFactorChallenge(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        PendingTwoFactorChallenges.TryRemove(key, out _);
    }

    private static string NormalizeTwoFactorCode(string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return string.Empty;
        }

        return new string(rawCode.Where(char.IsDigit).ToArray());
    }

    private static string NormalizeSecurityViolationReason(string? rawReason)
    {
        if (string.IsNullOrWhiteSpace(rawReason))
        {
            return "unspecified violation";
        }

        var normalized = rawReason.Trim();
        return normalized.Length > 256 ? normalized[..256] : normalized;
    }

    private static string NormalizeSecurityViolationEvidence(string? rawEvidence)
    {
        if (string.IsNullOrWhiteSpace(rawEvidence))
        {
            return string.Empty;
        }

        var normalized = rawEvidence.Trim();
        return normalized.Length > 2048 ? normalized[..2048] : normalized;
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

    private static string NormalizeHwidHash(string? rawHwidHash)
    {
        if (string.IsNullOrWhiteSpace(rawHwidHash))
        {
            return string.Empty;
        }

        var normalized = rawHwidHash.Trim().ToLowerInvariant();
        return normalized.Length > 128 ? normalized[..128] : normalized;
    }

    private static bool IsLegacyUsernameExternalId(AuthAccount account)
    {
        return string.IsNullOrWhiteSpace(account.ExternalId) ||
            string.Equals(account.ExternalId, account.Username, StringComparison.OrdinalIgnoreCase);
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

    private async Task<List<string>> ApplyConfiguredSecurityRolesAsync(
        string username,
        IEnumerable<string> roles,
        CancellationToken cancellationToken)
    {
        var normalizedRoles = NormalizeRoles(roles);
        if (string.IsNullOrWhiteSpace(username))
        {
            return normalizedRoles;
        }

        var securitySettings = await securitySettingsProvider.GetSettingsAsync(cancellationToken);
        if (securitySettings.LauncherAdminUsernames.Any(
                configuredUsername => string.Equals(
                    configuredUsername,
                    username,
                    StringComparison.OrdinalIgnoreCase)) &&
            !normalizedRoles.Contains("admin", StringComparer.OrdinalIgnoreCase))
        {
            normalizedRoles.Add("admin");
        }

        return normalizedRoles;
    }

    private sealed record PendingTwoFactorChallenge(
        Guid AccountId,
        List<string> Roles,
        DateTime ExpiresAtUtc);
}
