using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Infrastructure;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Api.Controllers;

[ApiController]
[EnableRateLimiting(RateLimitPolicies.PublicYggdrasilPolicy)]
public sealed class PublicYggdrasilController(
    AppDbContext dbContext,
    IConfiguration configuration,
    IDeliverySettingsProvider deliverySettingsProvider,
    IOptions<JwtOptions> jwtOptionsAccessor,
    ILogger<PublicYggdrasilController> logger) : ControllerBase
{
    private const string LauncherVerifiedClaimType = "launcher_verified";
    private const string LauncherVerifiedClaimValue = "1";
    private const string LauncherProofIdClaimType = "launcher_proof_id";
    private const string LauncherVersionClaimType = "launcher_version";
    private const string LauncherMinClientVersionConfigKey = "LAUNCHER_MIN_CLIENT_VERSION";
    private const string ForbiddenOperation = "ForbiddenOperationException";
    private const string IllegalArgument = "IllegalArgumentException";
    private const string LegacyJoinOk = "OK";
    private const string LegacyJoinBadLogin = "Bad login";
    private const string LegacyJoinBadRequest = "Bad request";
    private const string LegacyCheckYes = "YES";
    private const string LegacyCheckNo = "NO";
    private static readonly ConcurrentDictionary<string, JoinTicket> JoinTickets = new(StringComparer.Ordinal);
    private static readonly TimeSpan JoinTicketLifetime = TimeSpan.FromMinutes(3);

    private readonly JwtSecurityTokenHandler _jwtTokenHandler = new() { MapInboundClaims = false };
    private readonly TokenValidationParameters _tokenValidationParameters = BuildTokenValidationParameters(jwtOptionsAccessor.Value);
    private readonly IDeliverySettingsProvider _deliverySettingsProvider = deliverySettingsProvider;

    [HttpGet("/api/public/yggdrasil")]
    [HttpGet("/api/yggdrasil")]
    public IActionResult Metadata()
    {
        try
        {
            var publicBaseUrl = ResolvePublicBaseUrl(Request);
            var hostName = ResolveHostName(publicBaseUrl, Request.Host.Host);
            var signaturePublicKey = (configuration["YGGDRASIL_SIGNATURE_PUBLIC_KEY"] ?? string.Empty).Trim();
            var apiLocation = BuildApiLocation(publicBaseUrl);
            var metadata = new Dictionary<string, object?>
            {
                ["meta"] = new
                {
                    serverName = (configuration["YGGDRASIL_SERVER_NAME"] ?? "BivLauncher Auth").Trim(),
                    implementationName = "BivLauncher.Yggdrasil",
                    implementationVersion = "1.0.0",
                    links = new
                    {
                        homepage = publicBaseUrl
                    }
                },
                ["skinDomains"] = string.IsNullOrWhiteSpace(hostName)
                    ? new[] { "localhost" }
                    : new[] { hostName, "localhost" }
            };
            if (!string.IsNullOrWhiteSpace(signaturePublicKey))
            {
                metadata["signaturePublickey"] = signaturePublicKey;
            }

            if (!string.IsNullOrWhiteSpace(apiLocation))
            {
                Response.Headers["X-Authlib-Injector-API-Location"] = apiLocation;
            }

            return Ok(metadata);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate Yggdrasil metadata payload. Returning fallback metadata.");
            return Ok(new
            {
                meta = new
                {
                    serverName = "BivLauncher Auth",
                    implementationName = "BivLauncher.Yggdrasil",
                    implementationVersion = "1.0.0",
                    links = new
                    {
                        homepage = string.Empty
                    }
                },
                skinDomains = new[] { "localhost" }
            });
        }
    }

    [HttpPost("/authenticate")]
    [HttpPost("/authserver/authenticate")]
    [HttpPost("/api/public/authserver/authenticate")]
    [HttpPost("/api/public/yggdrasil/authenticate")]
    [HttpPost("/api/public/yggdrasil/authserver/authenticate")]
    [HttpPost("/api/yggdrasil/authserver/authenticate")]
    public IActionResult Authenticate()
    {
        return YggdrasilError(
            StatusCodes.Status403Forbidden,
            ForbiddenOperation,
            "Direct password authentication is disabled. Use launcher API login flow.",
            cause: string.Empty);
    }

    [HttpPost("/validate")]
    [HttpPost("/authserver/validate")]
    [HttpPost("/api/public/authserver/validate")]
    [HttpPost("/api/public/yggdrasil/validate")]
    [HttpPost("/api/public/yggdrasil/authserver/validate")]
    [HttpPost("/api/yggdrasil/authserver/validate")]
    public async Task<IActionResult> Validate(
        [FromBody] YggdrasilAccessTokenRequest? request,
        CancellationToken cancellationToken)
    {
        var token = request?.AccessToken ?? string.Empty;
        var result = await ValidatePlayerAccessTokenAsync(token, cancellationToken);
        if (!result.Success)
        {
            return YggdrasilError(
                StatusCodes.Status403Forbidden,
                ForbiddenOperation,
                result.ErrorMessage,
                cause: result.Cause);
        }

        return NoContent();
    }

    [HttpPost("/refresh")]
    [HttpPost("/authserver/refresh")]
    [HttpPost("/api/public/authserver/refresh")]
    [HttpPost("/api/public/yggdrasil/refresh")]
    [HttpPost("/api/public/yggdrasil/authserver/refresh")]
    [HttpPost("/api/yggdrasil/authserver/refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] YggdrasilRefreshRequest? request,
        CancellationToken cancellationToken)
    {
        var token = request?.AccessToken ?? string.Empty;
        var result = await ValidatePlayerAccessTokenAsync(token, cancellationToken);
        if (!result.Success || result.Account is null)
        {
            return YggdrasilError(
                StatusCodes.Status403Forbidden,
                ForbiddenOperation,
                result.ErrorMessage,
                cause: result.Cause);
        }

        var account = result.Account;
        var profile = BuildProfile(account);
        var clientToken = NormalizeClientToken(request?.ClientToken);
        var accessToken = ExtractAccessToken(token);

        return Ok(new
        {
            accessToken,
            clientToken,
            selectedProfile = profile,
            availableProfiles = new[] { profile },
            user = new
            {
                id = account.ExternalId,
                properties = Array.Empty<object>()
            }
        });
    }

    [HttpPost("/invalidate")]
    [HttpPost("/authserver/invalidate")]
    [HttpPost("/api/public/authserver/invalidate")]
    [HttpPost("/api/public/yggdrasil/invalidate")]
    [HttpPost("/api/public/yggdrasil/authserver/invalidate")]
    [HttpPost("/api/yggdrasil/authserver/invalidate")]
    public async Task<IActionResult> Invalidate(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] YggdrasilAccessTokenRequest? request,
        CancellationToken cancellationToken)
    {
        var token = (request?.AccessToken ?? string.Empty).Trim();
        if (HttpContext is not null && string.IsNullOrWhiteSpace(token))
        {
            token = await ReadLegacyParameterAsync(cancellationToken, "accessToken", "sessionId");
        }

        await TryRevokeSessionByTokenAsync(token, cancellationToken);
        return NoContent();
    }

    [HttpPost("/signout")]
    [HttpPost("/authserver/signout")]
    [HttpPost("/api/public/authserver/signout")]
    [HttpPost("/api/public/yggdrasil/signout")]
    [HttpPost("/api/public/yggdrasil/authserver/signout")]
    [HttpPost("/api/yggdrasil/authserver/signout")]
    public async Task<IActionResult> SignOutEndpoint(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] YggdrasilAccessTokenRequest? request,
        CancellationToken cancellationToken)
    {
        var token = (request?.AccessToken ?? string.Empty).Trim();
        if (HttpContext is not null && string.IsNullOrWhiteSpace(token))
        {
            token = await ReadLegacyParameterAsync(cancellationToken, "accessToken", "sessionId");
        }

        await TryRevokeSessionByTokenAsync(token, cancellationToken);
        return NoContent();
    }

    [HttpPost("/authserver/session/minecraft/join")]
    [HttpPost("/authserver/minecraft/join")]
    [HttpPost("/session/minecraft/join")]
    [HttpPost("/sessionserver/session/minecraft/join")]
    [HttpPost("/sessionserver/minecraft/join")]
    [HttpPost("/api/public/authserver/session/minecraft/join")]
    [HttpPost("/api/public/authserver/minecraft/join")]
    [HttpPost("/api/public/sessionserver/session/minecraft/join")]
    [HttpPost("/api/public/sessionserver/minecraft/join")]
    [HttpPost("/api/public/yggdrasil/authserver/session/minecraft/join")]
    [HttpPost("/api/public/yggdrasil/authserver/minecraft/join")]
    [HttpPost("/api/public/yggdrasil/minecraft/join")]
    [HttpPost("/api/public/yggdrasil/session/minecraft/join")]
    [HttpPost("/api/public/yggdrasil/sessionserver/session/minecraft/join")]
    [HttpPost("/api/public/yggdrasil/sessionserver/minecraft/join")]
    [HttpPost("/api/yggdrasil/authserver/session/minecraft/join")]
    [HttpPost("/api/yggdrasil/authserver/minecraft/join")]
    [HttpPost("/api/yggdrasil/minecraft/join")]
    [HttpPost("/api/yggdrasil/sessionserver/session/minecraft/join")]
    [HttpPost("/api/yggdrasil/sessionserver/minecraft/join")]
    public async Task<IActionResult> Join(
        [FromBody] YggdrasilJoinRequest? request,
        CancellationToken cancellationToken)
    {
        var accessToken = (request?.AccessToken ?? string.Empty).Trim();
        var serverId = (request?.ServerId ?? string.Empty).Trim();
        if (HttpContext is not null && string.IsNullOrWhiteSpace(accessToken))
        {
            accessToken = await ReadLegacyParameterAsync(cancellationToken, "sessionId", "accessToken");
        }

        if (HttpContext is not null && string.IsNullOrWhiteSpace(serverId))
        {
            serverId = await ReadLegacyParameterAsync(cancellationToken, "serverId");
        }

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(serverId))
        {
            logger.LogWarning("Yggdrasil join rejected: empty payload.");
            return YggdrasilError(
                StatusCodes.Status400BadRequest,
                IllegalArgument,
                "Join payload is required.",
                cause: string.Empty);
        }

        var result = await ValidatePlayerAccessTokenAsync(accessToken, cancellationToken);
        if (!result.Success || result.Account is null)
        {
            logger.LogWarning(
                "Yggdrasil join rejected: invalid token for serverId={ServerId}, reason={Reason}",
                serverId,
                result.ErrorMessage);
            return YggdrasilError(
                StatusCodes.Status403Forbidden,
                ForbiddenOperation,
                result.ErrorMessage,
                cause: result.Cause);
        }

        var account = result.Account;
        var profile = BuildProfile(account);
        var ticketKey = BuildTicketKey(account.Username, serverId);
        var now = DateTime.UtcNow;
        JoinTickets[ticketKey] = new JoinTicket(
            AccountId: account.Id,
            Username: account.Username,
            ServerId: serverId,
            ProfileId: profile.Id,
            SessionVersion: account.SessionVersion,
            ExpiresAtUtc: now.Add(JoinTicketLifetime));

        PruneExpiredTickets(now);
        logger.LogInformation(
            "Yggdrasil join accepted: username={Username}, serverId={ServerId}, profileId={ProfileId}",
            account.Username,
            serverId,
            profile.Id);
        return NoContent();
    }

    [HttpGet("/game/joinserver.jsp")]
    [HttpPost("/game/joinserver.jsp")]
    [HttpGet("/joinserver.jsp")]
    [HttpPost("/joinserver.jsp")]
    [HttpGet("/authserver/game/joinserver.jsp")]
    [HttpPost("/authserver/game/joinserver.jsp")]
    [HttpGet("/authserver/joinserver.jsp")]
    [HttpPost("/authserver/joinserver.jsp")]
    [HttpGet("/sessionserver/game/joinserver.jsp")]
    [HttpPost("/sessionserver/game/joinserver.jsp")]
    [HttpGet("/sessionserver/joinserver.jsp")]
    [HttpPost("/sessionserver/joinserver.jsp")]
    [HttpGet("/api/public/authserver/game/joinserver.jsp")]
    [HttpPost("/api/public/authserver/game/joinserver.jsp")]
    [HttpGet("/api/public/authserver/joinserver.jsp")]
    [HttpPost("/api/public/authserver/joinserver.jsp")]
    [HttpGet("/api/public/sessionserver/game/joinserver.jsp")]
    [HttpPost("/api/public/sessionserver/game/joinserver.jsp")]
    [HttpGet("/api/public/sessionserver/joinserver.jsp")]
    [HttpPost("/api/public/sessionserver/joinserver.jsp")]
    [HttpGet("/api/public/yggdrasil/authserver/game/joinserver.jsp")]
    [HttpPost("/api/public/yggdrasil/authserver/game/joinserver.jsp")]
    [HttpGet("/api/public/yggdrasil/authserver/joinserver.jsp")]
    [HttpPost("/api/public/yggdrasil/authserver/joinserver.jsp")]
    [HttpGet("/api/public/yggdrasil/game/joinserver.jsp")]
    [HttpPost("/api/public/yggdrasil/game/joinserver.jsp")]
    [HttpGet("/api/public/yggdrasil/joinserver.jsp")]
    [HttpPost("/api/public/yggdrasil/joinserver.jsp")]
    [HttpGet("/api/public/yggdrasil/sessionserver/game/joinserver.jsp")]
    [HttpPost("/api/public/yggdrasil/sessionserver/game/joinserver.jsp")]
    [HttpGet("/api/public/yggdrasil/sessionserver/joinserver.jsp")]
    [HttpPost("/api/public/yggdrasil/sessionserver/joinserver.jsp")]
    [HttpGet("/api/yggdrasil/authserver/game/joinserver.jsp")]
    [HttpPost("/api/yggdrasil/authserver/game/joinserver.jsp")]
    [HttpGet("/api/yggdrasil/authserver/joinserver.jsp")]
    [HttpPost("/api/yggdrasil/authserver/joinserver.jsp")]
    [HttpGet("/api/yggdrasil/game/joinserver.jsp")]
    [HttpPost("/api/yggdrasil/game/joinserver.jsp")]
    [HttpGet("/api/yggdrasil/joinserver.jsp")]
    [HttpPost("/api/yggdrasil/joinserver.jsp")]
    [HttpGet("/api/yggdrasil/sessionserver/game/joinserver.jsp")]
    [HttpPost("/api/yggdrasil/sessionserver/game/joinserver.jsp")]
    [HttpGet("/api/yggdrasil/sessionserver/joinserver.jsp")]
    [HttpPost("/api/yggdrasil/sessionserver/joinserver.jsp")]
    public async Task<IActionResult> LegacyJoinServer(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Legacy join endpoint hit: {Path}{Query}",
            Request.Path.Value ?? string.Empty,
            Request.QueryString.Value ?? string.Empty);

        var response = await Join(
            new YggdrasilJoinRequest
            {
                AccessToken = await ReadLegacyParameterAsync(cancellationToken, "sessionId", "accessToken"),
                SelectedProfile = await ReadLegacyParameterAsync(cancellationToken, "selectedProfile"),
                ServerId = await ReadLegacyParameterAsync(cancellationToken, "serverId")
            },
            cancellationToken);

        if (response is StatusCodeResult statusCodeResult &&
            statusCodeResult.StatusCode == StatusCodes.Status204NoContent)
        {
            return LegacyText(LegacyJoinOk);
        }

        if (response is OkObjectResult)
        {
            return LegacyText(LegacyJoinOk);
        }

        if (response is ObjectResult objectResult &&
            objectResult.StatusCode == StatusCodes.Status400BadRequest)
        {
            return LegacyText(LegacyJoinBadRequest);
        }

        return LegacyText(LegacyJoinBadLogin);
    }

    [HttpGet("/authserver/session/minecraft/hasJoined")]
    [HttpGet("/authserver/minecraft/hasJoined")]
    [HttpGet("/session/minecraft/hasJoined")]
    [HttpGet("/sessionserver/session/minecraft/hasJoined")]
    [HttpGet("/sessionserver/minecraft/hasJoined")]
    [HttpGet("/api/public/authserver/session/minecraft/hasJoined")]
    [HttpGet("/api/public/authserver/minecraft/hasJoined")]
    [HttpGet("/api/public/sessionserver/session/minecraft/hasJoined")]
    [HttpGet("/api/public/sessionserver/minecraft/hasJoined")]
    [HttpGet("/api/public/yggdrasil/authserver/session/minecraft/hasJoined")]
    [HttpGet("/api/public/yggdrasil/authserver/minecraft/hasJoined")]
    [HttpGet("/api/public/yggdrasil/minecraft/hasJoined")]
    [HttpGet("/api/public/yggdrasil/session/minecraft/hasJoined")]
    [HttpGet("/api/public/yggdrasil/sessionserver/session/minecraft/hasJoined")]
    [HttpGet("/api/public/yggdrasil/sessionserver/minecraft/hasJoined")]
    [HttpGet("/api/yggdrasil/authserver/session/minecraft/hasJoined")]
    [HttpGet("/api/yggdrasil/authserver/minecraft/hasJoined")]
    [HttpGet("/api/yggdrasil/minecraft/hasJoined")]
    [HttpGet("/api/yggdrasil/sessionserver/session/minecraft/hasJoined")]
    [HttpGet("/api/yggdrasil/sessionserver/minecraft/hasJoined")]
    public async Task<IActionResult> HasJoined(
        [FromQuery] string? username,
        [FromQuery] string? serverId,
        CancellationToken cancellationToken)
    {
        var legacyUsername = HttpContext is null
            ? string.Empty
            : (Request.Query["user"].ToString() ?? string.Empty).Trim();
        var normalizedUsername = NormalizeUsername(
            string.IsNullOrWhiteSpace(username)
                ? legacyUsername
                : username);
        var normalizedServerId = (serverId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(normalizedServerId))
        {
            logger.LogDebug(
                "Yggdrasil hasJoined miss: missing query params username='{Username}' serverId='{ServerId}'",
                normalizedUsername,
                normalizedServerId);
            return NoContent();
        }

        var ticketKey = BuildTicketKey(normalizedUsername, normalizedServerId);
        if (!JoinTickets.TryGetValue(ticketKey, out var ticket))
        {
            logger.LogWarning(
                "Yggdrasil hasJoined miss: no join ticket username={Username}, serverId={ServerId}",
                normalizedUsername,
                normalizedServerId);
            return NoContent();
        }

        if (ticket.ExpiresAtUtc <= DateTime.UtcNow)
        {
            JoinTickets.TryRemove(ticketKey, out _);
            logger.LogWarning(
                "Yggdrasil hasJoined miss: expired ticket username={Username}, serverId={ServerId}",
                normalizedUsername,
                normalizedServerId);
            return NoContent();
        }

        var account = await dbContext.AuthAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ticket.AccountId, cancellationToken);
        if (account is null)
        {
            JoinTickets.TryRemove(ticketKey, out _);
            logger.LogWarning(
                "Yggdrasil hasJoined miss: account not found for ticket username={Username}, serverId={ServerId}",
                normalizedUsername,
                normalizedServerId);
            return NoContent();
        }

        if (account.SessionVersion != ticket.SessionVersion)
        {
            JoinTickets.TryRemove(ticketKey, out _);
            logger.LogWarning(
                "Yggdrasil hasJoined miss: session version mismatch username={Username}, serverId={ServerId}",
                normalizedUsername,
                normalizedServerId);
            return NoContent();
        }

        if (!await IsAccountStateAllowedAsync(account, cancellationToken))
        {
            JoinTickets.TryRemove(ticketKey, out _);
            logger.LogWarning(
                "Yggdrasil hasJoined miss: account denied username={Username}, serverId={ServerId}",
                normalizedUsername,
                normalizedServerId);
            return NoContent();
        }

        logger.LogInformation(
            "Yggdrasil hasJoined hit: username={Username}, serverId={ServerId}, profileId={ProfileId}",
            account.Username,
            normalizedServerId,
            ticket.ProfileId);
        return Ok(new
        {
            id = ticket.ProfileId,
            name = account.Username,
            properties = Array.Empty<object>()
        });
    }

    [HttpGet("/game/checkserver.jsp")]
    [HttpGet("/checkserver.jsp")]
    [HttpGet("/authserver/game/checkserver.jsp")]
    [HttpGet("/authserver/checkserver.jsp")]
    [HttpGet("/sessionserver/game/checkserver.jsp")]
    [HttpGet("/sessionserver/checkserver.jsp")]
    [HttpGet("/api/public/authserver/game/checkserver.jsp")]
    [HttpGet("/api/public/authserver/checkserver.jsp")]
    [HttpGet("/api/public/sessionserver/game/checkserver.jsp")]
    [HttpGet("/api/public/sessionserver/checkserver.jsp")]
    [HttpGet("/api/public/yggdrasil/authserver/game/checkserver.jsp")]
    [HttpGet("/api/public/yggdrasil/authserver/checkserver.jsp")]
    [HttpGet("/api/public/yggdrasil/game/checkserver.jsp")]
    [HttpGet("/api/public/yggdrasil/checkserver.jsp")]
    [HttpGet("/api/public/yggdrasil/sessionserver/game/checkserver.jsp")]
    [HttpGet("/api/public/yggdrasil/sessionserver/checkserver.jsp")]
    [HttpGet("/api/yggdrasil/authserver/game/checkserver.jsp")]
    [HttpGet("/api/yggdrasil/authserver/checkserver.jsp")]
    [HttpGet("/api/yggdrasil/game/checkserver.jsp")]
    [HttpGet("/api/yggdrasil/checkserver.jsp")]
    [HttpGet("/api/yggdrasil/sessionserver/game/checkserver.jsp")]
    [HttpGet("/api/yggdrasil/sessionserver/checkserver.jsp")]
    public async Task<IActionResult> LegacyCheckServer(
        [FromQuery(Name = "user")] string? username,
        [FromQuery] string? serverId,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Legacy check endpoint hit: {Path}{Query}",
            Request.Path.Value ?? string.Empty,
            Request.QueryString.Value ?? string.Empty);

        var response = await HasJoined(username, serverId, cancellationToken);
        return response is OkObjectResult
            ? LegacyText(LegacyCheckYes)
            : LegacyText(LegacyCheckNo);
    }

    [HttpGet("/authserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/authserver/minecraft/profile/{profileId}")]
    [HttpGet("/session/minecraft/profile/{profileId}")]
    [HttpGet("/sessionserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/sessionserver/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/authserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/authserver/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/sessionserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/sessionserver/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/yggdrasil/authserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/yggdrasil/authserver/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/yggdrasil/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/yggdrasil/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/yggdrasil/sessionserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/yggdrasil/sessionserver/minecraft/profile/{profileId}")]
    [HttpGet("/api/yggdrasil/authserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/yggdrasil/authserver/minecraft/profile/{profileId}")]
    [HttpGet("/api/yggdrasil/minecraft/profile/{profileId}")]
    [HttpGet("/api/yggdrasil/sessionserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/yggdrasil/sessionserver/minecraft/profile/{profileId}")]
    public async Task<IActionResult> Profile(
        string profileId,
        CancellationToken cancellationToken)
    {
        var normalizedProfileId = NormalizeProfileId(profileId);
        if (string.IsNullOrWhiteSpace(normalizedProfileId))
        {
            return NotFound(new { error = "Profile not found." });
        }

        var directExternalMatches = BuildDirectExternalIdCandidates(normalizedProfileId);
        var directAccount = await dbContext.AuthAccounts
            .AsNoTracking()
            .Where(x => directExternalMatches.Contains(x.ExternalId))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (directAccount is not null)
        {
            return Ok(await BuildProfileResponseAsync(
                normalizedProfileId,
                directAccount.Id,
                directAccount.Username,
                cancellationToken));
        }

        var accountCandidates = await dbContext.AuthAccounts
            .AsNoTracking()
            .Select(x => new { x.Id, x.Username, x.ExternalId })
            .ToListAsync(cancellationToken);
        var fallbackAccount = accountCandidates.FirstOrDefault(x =>
            string.Equals(
                ResolveLegacyProfileId(x.ExternalId, x.Username),
                normalizedProfileId,
                StringComparison.OrdinalIgnoreCase));
        if (fallbackAccount is null)
        {
            return NotFound(new { error = "Profile not found." });
        }

        return Ok(await BuildProfileResponseAsync(
            normalizedProfileId,
            fallbackAccount.Id,
            fallbackAccount.Username,
            cancellationToken));
    }

    private async Task<object> BuildProfileResponseAsync(
        string profileId,
        Guid accountId,
        string username,
        CancellationToken cancellationToken)
    {
        var publicBaseUrl = ResolvePublicBaseUrl(Request).TrimEnd('/');
        var hasSkin = await dbContext.SkinAssets
            .AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId, cancellationToken);
        var hasCape = await dbContext.CapeAssets
            .AsNoTracking()
            .AnyAsync(x => x.AccountId == accountId, cancellationToken);

        var properties = BuildTextureProperties(
            publicBaseUrl,
            profileId,
            username,
            hasSkin,
            hasCape);

        return new
        {
            id = profileId,
            name = username,
            properties
        };
    }

    private static object[] BuildTextureProperties(
        string publicBaseUrl,
        string profileId,
        string username,
        bool hasSkin,
        bool hasCape)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl) || (!hasSkin && !hasCape))
        {
            return Array.Empty<object>();
        }

        var textures = new Dictionary<string, object>(StringComparer.Ordinal);
        var encodedUsername = Uri.EscapeDataString(username);
        if (hasSkin)
        {
            textures["SKIN"] = new
            {
                url = $"{publicBaseUrl}/skins/{encodedUsername}.png"
            };
        }

        if (hasCape)
        {
            textures["CAPE"] = new
            {
                url = $"{publicBaseUrl}/capes/{encodedUsername}.png"
            };
        }

        var payload = new
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            profileId,
            profileName = username,
            textures
        };
        var serialized = JsonSerializer.Serialize(payload);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(serialized));

        return
        [
            new
            {
                name = "textures",
                value = encoded
            }
        ];
    }

    private async Task<TokenValidationResult> ValidatePlayerAccessTokenAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        var accessToken = ExtractAccessToken(rawToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return TokenValidationResult.Fail("Access token is required.", cause: string.Empty);
        }

        ClaimsPrincipal principal;
        try
        {
            principal = _jwtTokenHandler.ValidateToken(
                accessToken,
                _tokenValidationParameters,
                out _);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Yggdrasil token validation failed.");
            return TokenValidationResult.Fail("Invalid access token.", cause: string.Empty);
        }

        var externalId = principal.FindFirstValue("external_id")?.Trim() ?? string.Empty;
        var username =
            principal.FindFirstValue(ClaimTypes.Name)?.Trim() ??
            principal.FindFirstValue(JwtRegisteredClaimNames.UniqueName)?.Trim() ??
            string.Empty;
        if (string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(username))
        {
            return TokenValidationResult.Fail("Invalid access token identity payload.", cause: string.Empty);
        }

        var requiredProof = (configuration["LAUNCHER_CLIENT_PROOF"] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(requiredProof))
        {
            var launcherVerified = principal.FindFirstValue(LauncherVerifiedClaimType)?.Trim() ?? string.Empty;
            if (!string.Equals(launcherVerified, LauncherVerifiedClaimValue, StringComparison.Ordinal) ||
                !IsTokenLauncherProofAllowed(principal))
            {
                return TokenValidationResult.Fail("Access token session expired. Login again.", cause: string.Empty);
            }
        }

        if (!IsTokenLauncherVersionAllowed(principal))
        {
            return TokenValidationResult.Fail("Access token session expired. Login again.", cause: string.Empty);
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
            return TokenValidationResult.Fail("Account not found for access token.", cause: string.Empty);
        }

        var tokenSessionVersionRaw = principal.FindFirstValue("session_version");
        if (!TryParseSessionVersion(tokenSessionVersionRaw, out var tokenSessionVersion) ||
            tokenSessionVersion != account.SessionVersion)
        {
            return TokenValidationResult.Fail("Access token session expired. Login again.", cause: string.Empty);
        }

        if (!await IsAccountStateAllowedAsync(account, cancellationToken))
        {
            return TokenValidationResult.Fail("Account access denied.", cause: string.Empty);
        }

        return TokenValidationResult.Ok(account);
    }

    private async Task<bool> IsAccountStateAllowedAsync(AuthAccount account, CancellationToken cancellationToken)
    {
        if (account.Banned)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var hasAccountBan = await dbContext.HardwareBans
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.AccountId == account.Id &&
                    (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now),
                cancellationToken);
        if (hasAccountBan)
        {
            return false;
        }

        var normalizedDeviceUserName = NormalizeDeviceUserName(account.DeviceUserName);
        if (string.IsNullOrWhiteSpace(normalizedDeviceUserName))
        {
            return true;
        }

        var hasDeviceUserBan = await dbContext.HardwareBans
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.DeviceUserName == normalizedDeviceUserName &&
                    (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now),
                cancellationToken);
        return !hasDeviceUserBan;
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

    private string ResolveLauncherProofId()
    {
        var requiredProof = (configuration["LAUNCHER_CLIENT_PROOF"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requiredProof))
        {
            return string.Empty;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(requiredProof));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static TokenValidationParameters BuildTokenValidationParameters(JwtOptions options)
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret)),
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    private string ResolvePublicBaseUrl(HttpRequest request)
    {
        var forwardedProto = TryGetForwardedHeaderValue(request, "X-Forwarded-Proto");
        var forwardedHost = TryGetForwardedHeaderValue(request, "X-Forwarded-Host");
        var forwardedPrefix = TryGetForwardedHeaderValue(request, "X-Forwarded-Prefix");

        var scheme = !string.IsNullOrWhiteSpace(forwardedProto)
            ? forwardedProto
            : (string.IsNullOrWhiteSpace(request.Scheme) ? "http" : request.Scheme);
        var host = !string.IsNullOrWhiteSpace(forwardedHost)
            ? forwardedHost
            : (request.Host.HasValue ? request.Host.Value : "localhost:8080");
        var pathBase = !string.IsNullOrWhiteSpace(forwardedPrefix)
            ? forwardedPrefix
            : request.PathBase.Value;
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost:8080";
        }

        var normalizedPathBase = string.IsNullOrWhiteSpace(pathBase)
            ? string.Empty
            : "/" + pathBase.Trim().Trim('/');
        return $"{scheme}://{host}{normalizedPathBase}".TrimEnd('/');
    }

    private static string ResolveHostName(string publicBaseUrl, string requestHost)
    {
        if (Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var parsed) &&
            !string.IsNullOrWhiteSpace(parsed.Host))
        {
            return parsed.Host.Trim().ToLowerInvariant();
        }

        return string.IsNullOrWhiteSpace(requestHost)
            ? string.Empty
            : requestHost.Trim().ToLowerInvariant();
    }

    private static string BuildApiLocation(string publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            return string.Empty;
        }

        return $"{publicBaseUrl.TrimEnd('/')}/api/public/yggdrasil/";
    }

    private static string TryGetForwardedHeaderValue(HttpRequest request, string headerName)
    {
        if (!request.Headers.TryGetValue(headerName, out var values))
        {
            return string.Empty;
        }

        var rawValue = values.ToString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var firstValue = rawValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return (firstValue ?? string.Empty).Trim();
    }

    private static string NormalizeClientToken(string? clientToken)
    {
        var normalized = (clientToken ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? Guid.NewGuid().ToString("N")
            : normalized;
    }

    private static string ExtractAccessToken(string rawToken)
    {
        var normalized = (rawToken ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["bearer ".Length..].Trim();
        }

        if (!normalized.StartsWith("token:", StringComparison.OrdinalIgnoreCase))
        {
            if (TryExtractJwtFromColonSeparated(normalized, out var jwtFromRaw))
            {
                return jwtFromRaw;
            }

            return normalized;
        }

        var payload = normalized["token:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        if (TryExtractJwtFromColonSeparated(payload, out var jwtFromTokenPayload))
        {
            return jwtFromTokenPayload;
        }

        var parts = payload
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (parts.Length == 1)
        {
            return payload.Trim();
        }

        var firstPart = parts[0];
        var secondPart = parts[1];

        if (LooksLikeProfileId(firstPart) && !LooksLikeProfileId(secondPart))
        {
            return secondPart;
        }

        if (LooksLikeProfileId(secondPart) && !LooksLikeProfileId(firstPart))
        {
            return firstPart;
        }

        return firstPart;
    }

    private static bool TryExtractJwtFromColonSeparated(string payload, out string jwt)
    {
        jwt = string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!LooksLikeJwt(part))
            {
                continue;
            }

            jwt = part;
            return true;
        }

        return false;
    }

    private static bool LooksLikeJwt(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var segments = token.Split('.');
        if (segments.Length != 3)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return false;
            }

            foreach (var ch in segment)
            {
                var isBase64Url = (ch >= 'a' && ch <= 'z') ||
                                  (ch >= 'A' && ch <= 'Z') ||
                                  (ch >= '0' && ch <= '9') ||
                                  ch is '-' or '_';
                if (!isBase64Url)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool LooksLikeProfileId(string value)
    {
        var normalized = NormalizeProfileId(value);
        return !string.IsNullOrWhiteSpace(normalized);
    }

    private static string NormalizeUsername(string? username)
    {
        return (username ?? string.Empty).Trim();
    }

    private static string BuildTicketKey(string username, string serverId)
    {
        return $"{username.Trim().ToLowerInvariant()}|{serverId.Trim()}";
    }

    private static void PruneExpiredTickets(DateTime now)
    {
        foreach (var entry in JoinTickets)
        {
            if (entry.Value.ExpiresAtUtc > now)
            {
                continue;
            }

            JoinTickets.TryRemove(entry.Key, out _);
        }
    }

    private static void RevokeJoinTicketsForAccount(Guid accountId)
    {
        foreach (var entry in JoinTickets)
        {
            if (entry.Value.AccountId != accountId)
            {
                continue;
            }

            JoinTickets.TryRemove(entry.Key, out _);
        }
    }

    private static bool TryParseSessionVersion(string? rawValue, out int sessionVersion)
    {
        sessionVersion = 0;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        return int.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sessionVersion) &&
               sessionVersion >= 0;
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

    private async Task<bool> TryRevokeSessionByTokenAsync(string rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        var tokenValidation = await ValidatePlayerAccessTokenAsync(rawToken, cancellationToken);
        if (!tokenValidation.Success || tokenValidation.Account is null)
        {
            return false;
        }

        var account = await dbContext.AuthAccounts
            .FirstOrDefaultAsync(x => x.Id == tokenValidation.Account.Id, cancellationToken);
        if (account is null)
        {
            return false;
        }

        account.SessionVersion++;
        account.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        RevokeJoinTicketsForAccount(account.Id);
        return true;
    }

    private static YggdrasilProfile BuildProfile(AuthAccount account)
    {
        return new YggdrasilProfile(
            Id: ResolveLegacyProfileId(account.ExternalId, account.Username),
            Name: account.Username,
            Legacy: true);
    }

    private static string ResolveLegacyProfileId(string externalId, string username)
    {
        var candidate = (externalId ?? string.Empty).Trim();
        if (Guid.TryParse(candidate, out var parsedGuid))
        {
            return parsedGuid.ToString("N");
        }

        var hexOnly = new string(candidate.Where(IsHexChar).ToArray());
        if (hexOnly.Length == 32)
        {
            return hexOnly.ToLowerInvariant();
        }

        var source = "OfflinePlayer:" + (string.IsNullOrWhiteSpace(username) ? "Player" : username.Trim());
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(source));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var offlineGuid = new Guid(hash);
        return offlineGuid.ToString("N");
    }

    private static bool IsHexChar(char ch)
    {
        return (ch >= '0' && ch <= '9') ||
               (ch >= 'a' && ch <= 'f') ||
               (ch >= 'A' && ch <= 'F');
    }

    private static string NormalizeProfileId(string? rawProfileId)
    {
        var candidate = (rawProfileId ?? string.Empty).Trim();
        if (Guid.TryParse(candidate, out var parsedGuid))
        {
            return parsedGuid.ToString("N");
        }

        var hexOnly = new string(candidate.Where(IsHexChar).ToArray());
        return hexOnly.Length == 32
            ? hexOnly.ToLowerInvariant()
            : string.Empty;
    }

    private static IReadOnlyCollection<string> BuildDirectExternalIdCandidates(string normalizedProfileId)
    {
        if (string.IsNullOrWhiteSpace(normalizedProfileId))
        {
            return [];
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalizedProfileId
        };

        if (Guid.TryParseExact(normalizedProfileId, "N", out var guid))
        {
            candidates.Add(guid.ToString("D"));
            candidates.Add(guid.ToString("B"));
            candidates.Add(guid.ToString("P"));
            candidates.Add(guid.ToString("N"));
        }

        return candidates;
    }

    private async Task<string> ReadLegacyParameterAsync(CancellationToken cancellationToken, params string[] keys)
    {
        foreach (var key in keys)
        {
            var queryValue = (Request.Query[key].ToString() ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(queryValue))
            {
                return queryValue;
            }
        }

        if (!Request.HasFormContentType)
        {
            return string.Empty;
        }

        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            foreach (var key in keys)
            {
                var formValue = (form[key].ToString() ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(formValue))
                {
                    return formValue;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read legacy joinserver/checkserver form payload.");
        }

        return string.Empty;
    }

    private static ContentResult LegacyText(string payload)
    {
        return new ContentResult
        {
            ContentType = "text/plain; charset=utf-8",
            Content = payload
        };
    }

    private IActionResult YggdrasilError(
        int statusCode,
        string error,
        string errorMessage,
        string cause)
    {
        return StatusCode(statusCode, new
        {
            error,
            errorMessage,
            cause
        });
    }

    public sealed class YggdrasilAccessTokenRequest
    {
        public string AccessToken { get; set; } = string.Empty;
        public string ClientToken { get; set; } = string.Empty;
    }

    public sealed class YggdrasilRefreshRequest
    {
        public string AccessToken { get; set; } = string.Empty;
        public string ClientToken { get; set; } = string.Empty;
        public string SelectedProfile { get; set; } = string.Empty;
        public bool RequestUser { get; set; }
    }

    public sealed class YggdrasilJoinRequest
    {
        public string AccessToken { get; set; } = string.Empty;
        public string SelectedProfile { get; set; } = string.Empty;
        public string ServerId { get; set; } = string.Empty;
    }

    private sealed record JoinTicket(
        Guid AccountId,
        string Username,
        string ServerId,
        string ProfileId,
        int SessionVersion,
        DateTime ExpiresAtUtc);

    private sealed record YggdrasilProfile(
        string Id,
        string Name,
        bool Legacy);

    private sealed record TokenValidationResult(
        bool Success,
        AuthAccount? Account,
        string ErrorMessage,
        string Cause)
    {
        public static TokenValidationResult Ok(AuthAccount account)
        {
            return new TokenValidationResult(true, account, string.Empty, string.Empty);
        }

        public static TokenValidationResult Fail(string errorMessage, string cause)
        {
            return new TokenValidationResult(false, null, errorMessage, cause);
        }
    }
}
