using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Infrastructure;
using BivLauncher.Api.Options;
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

namespace BivLauncher.Api.Controllers;

[ApiController]
[EnableRateLimiting(RateLimitPolicies.PublicLoginPolicy)]
public sealed class PublicYggdrasilController(
    AppDbContext dbContext,
    IConfiguration configuration,
    IOptions<JwtOptions> jwtOptionsAccessor,
    ILogger<PublicYggdrasilController> logger) : ControllerBase
{
    private const string ForbiddenOperation = "ForbiddenOperationException";
    private const string IllegalArgument = "IllegalArgumentException";
    private static readonly ConcurrentDictionary<string, JoinTicket> JoinTickets = new(StringComparer.Ordinal);
    private static readonly TimeSpan JoinTicketLifetime = TimeSpan.FromMinutes(3);

    private readonly JwtSecurityTokenHandler _jwtTokenHandler = new() { MapInboundClaims = false };
    private readonly TokenValidationParameters _tokenValidationParameters = BuildTokenValidationParameters(jwtOptionsAccessor.Value);

    [HttpGet("/api/public/yggdrasil")]
    [HttpGet("/api/yggdrasil")]
    public IActionResult Metadata()
    {
        try
        {
            var publicBaseUrl = ResolvePublicBaseUrl(configuration, Request);
            var hostName = ResolveHostName(publicBaseUrl, Request.Host.Host);
            var signaturePublicKey = (configuration["YGGDRASIL_SIGNATURE_PUBLIC_KEY"] ?? string.Empty).Trim();
            var apiLocation = BuildApiLocation(publicBaseUrl);
            var metadata = new
            {
                meta = new
                {
                    serverName = (configuration["YGGDRASIL_SERVER_NAME"] ?? "BivLauncher Auth").Trim(),
                    implementationName = "BivLauncher.Yggdrasil",
                    implementationVersion = "1.0.0",
                    links = new
                    {
                        homepage = publicBaseUrl
                    }
                },
                skinDomains = string.IsNullOrWhiteSpace(hostName)
                    ? new[] { "localhost" }
                    : new[] { hostName, "localhost" },
                signaturePublickey = signaturePublicKey
            };

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
                skinDomains = new[] { "localhost" },
                signaturePublickey = string.Empty
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

        return Ok(new { });
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
    public IActionResult Invalidate()
    {
        return Ok(new { });
    }

    [HttpPost("/signout")]
    [HttpPost("/authserver/signout")]
    [HttpPost("/api/public/authserver/signout")]
    [HttpPost("/api/public/yggdrasil/signout")]
    [HttpPost("/api/public/yggdrasil/authserver/signout")]
    [HttpPost("/api/yggdrasil/authserver/signout")]
    public IActionResult SignOutEndpoint()
    {
        return Ok(new { });
    }

    [HttpPost("/session/minecraft/join")]
    [HttpPost("/sessionserver/session/minecraft/join")]
    [HttpPost("/api/public/sessionserver/session/minecraft/join")]
    [HttpPost("/api/public/yggdrasil/session/minecraft/join")]
    [HttpPost("/api/public/yggdrasil/sessionserver/session/minecraft/join")]
    [HttpPost("/api/yggdrasil/sessionserver/session/minecraft/join")]
    public async Task<IActionResult> Join(
        [FromBody] YggdrasilJoinRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return YggdrasilError(
                StatusCodes.Status400BadRequest,
                IllegalArgument,
                "Join payload is required.",
                cause: string.Empty);
        }

        var serverId = (request.ServerId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(serverId))
        {
            return YggdrasilError(
                StatusCodes.Status400BadRequest,
                IllegalArgument,
                "serverId is required.",
                cause: string.Empty);
        }

        var result = await ValidatePlayerAccessTokenAsync(request.AccessToken, cancellationToken);
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
        return Ok(new { });
    }

    [HttpGet("/session/minecraft/hasJoined")]
    [HttpGet("/sessionserver/session/minecraft/hasJoined")]
    [HttpGet("/api/public/sessionserver/session/minecraft/hasJoined")]
    [HttpGet("/api/public/yggdrasil/session/minecraft/hasJoined")]
    [HttpGet("/api/public/yggdrasil/sessionserver/session/minecraft/hasJoined")]
    [HttpGet("/api/yggdrasil/sessionserver/session/minecraft/hasJoined")]
    public async Task<IActionResult> HasJoined(
        [FromQuery] string? username,
        [FromQuery] string? serverId,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = NormalizeUsername(username);
        var normalizedServerId = (serverId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(normalizedServerId))
        {
            return Ok(new { id = (string?)null });
        }

        var ticketKey = BuildTicketKey(normalizedUsername, normalizedServerId);
        if (!JoinTickets.TryGetValue(ticketKey, out var ticket))
        {
            return Ok(new { id = (string?)null });
        }

        if (ticket.ExpiresAtUtc <= DateTime.UtcNow)
        {
            JoinTickets.TryRemove(ticketKey, out _);
            return Ok(new { id = (string?)null });
        }

        var account = await dbContext.AuthAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ticket.AccountId, cancellationToken);
        if (account is null)
        {
            JoinTickets.TryRemove(ticketKey, out _);
            return Ok(new { id = (string?)null });
        }

        if (account.SessionVersion != ticket.SessionVersion)
        {
            JoinTickets.TryRemove(ticketKey, out _);
            return Ok(new { id = (string?)null });
        }

        if (!await IsAccountStateAllowedAsync(account, cancellationToken))
        {
            JoinTickets.TryRemove(ticketKey, out _);
            return Ok(new { id = (string?)null });
        }

        JoinTickets.TryRemove(ticketKey, out _);
        return Ok(new
        {
            id = ticket.ProfileId,
            name = account.Username,
            properties = Array.Empty<object>()
        });
    }

    [HttpGet("/session/minecraft/profile/{profileId}")]
    [HttpGet("/sessionserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/sessionserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/yggdrasil/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/public/yggdrasil/sessionserver/session/minecraft/profile/{profileId}")]
    [HttpGet("/api/yggdrasil/sessionserver/session/minecraft/profile/{profileId}")]
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
            return Ok(new
            {
                id = normalizedProfileId,
                name = directAccount.Username,
                properties = Array.Empty<object>()
            });
        }

        var accountCandidates = await dbContext.AuthAccounts
            .AsNoTracking()
            .Select(x => new { x.Username, x.ExternalId })
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

        return Ok(new
        {
            id = normalizedProfileId,
            name = fallbackAccount.Username,
            properties = Array.Empty<object>()
        });
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

    private static string ResolvePublicBaseUrl(IConfiguration configuration, HttpRequest request)
    {
        var configuredUrl = (configuration["PUBLIC_BASE_URL"] ?? configuration["PublicBaseUrl"] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            return configuredUrl.TrimEnd('/');
        }

        var scheme = string.IsNullOrWhiteSpace(request.Scheme) ? "http" : request.Scheme;
        var host = request.Host.HasValue ? request.Host.Value : "localhost:8080";
        return $"{scheme}://{host}";
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
            return normalized;
        }

        var payload = normalized["token:".Length..];
        var separatorIndex = payload.IndexOf(':');
        if (separatorIndex < 0)
        {
            return payload.Trim();
        }

        return payload[..separatorIndex].Trim();
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
