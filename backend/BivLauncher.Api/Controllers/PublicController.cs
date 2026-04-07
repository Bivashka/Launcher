using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Infrastructure;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Route("api/public")]
public sealed class PublicController(
    AppDbContext dbContext,
    IBrandingProvider brandingProvider,
    IBuildPipelineService buildPipelineService,
    ILauncherUpdateConfigProvider launcherUpdateConfigProvider,
    IConfiguration configuration,
    IDeliverySettingsProvider deliverySettingsProvider,
    IAssetUrlService assetUrlService,
    IObjectStorageService objectStorageService,
    IOptions<InstallTelemetryOptions> installTelemetryOptions,
    IOptions<DiscordRpcOptions> discordRpcOptions,
    ILogger<PublicController> logger) : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ManifestBuildLocks = new();
    private static readonly JsonSerializerOptions PublicJsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("bootstrap")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<ActionResult<BootstrapResponse>> Bootstrap(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        var currentUsername = ResolveCurrentPlayerUsername();

        var profiles = await dbContext.Profiles
            .AsNoTracking()
            .Include(x => x.Servers.Where(server => server.Enabled))
            .Where(x => x.Enabled)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);
        profiles = profiles
            .Where(profile => ProfileAccessRules.CanAccess(profile, currentUsername))
            .ToList();

        var profileIds = profiles.Select(x => x.Id).ToHashSet();
        var serverIds = profiles.SelectMany(x => x.Servers).Select(x => x.Id).ToHashSet();

        var discordConfigs = await dbContext.DiscordRpcConfigs
            .AsNoTracking()
            .Where(x =>
                (x.ScopeType == "profile" && profileIds.Contains(x.ScopeId)) ||
                (x.ScopeType == "server" && serverIds.Contains(x.ScopeId)))
            .ToListAsync(cancellationToken);

        var allNews = await dbContext.NewsItems
            .AsNoTracking()
            .Where(x => x.Enabled)
            .OrderByDescending(x => x.Pinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(60)
            .ToListAsync(cancellationToken);
        var news = FilterRelevantNews(allNews, profileIds, serverIds)
            .Take(20)
            .ToList();

        var branding = await brandingProvider.GetBrandingAsync(cancellationToken);
        branding = MapBranding(branding);
        var deliverySettings = deliverySettingsProvider.GetCachedSettings();
        var publicBaseUrl = ResolvePublicBaseUrl(deliverySettings);
        var installTelemetryEnabled = await ResolveInstallTelemetryEnabledAsync(cancellationToken);
        var discordRpcSettings = await ResolveDiscordRpcSettingsAsync(cancellationToken);
        var profileDiscord = discordConfigs
            .Where(x => x.ScopeType == "profile")
            .ToDictionary(
                x => x.ScopeId,
                x => MapDiscord(x, discordRpcSettings.Enabled, discordRpcSettings.PrivacyMode));
        var serverDiscord = discordConfigs
            .Where(x => x.ScopeType == "server")
            .ToDictionary(
                x => x.ScopeId,
                x => MapDiscord(x, discordRpcSettings.Enabled, discordRpcSettings.PrivacyMode));

        var response = new BootstrapResponse(
            PublicBaseUrl: publicBaseUrl,
            LauncherApiBaseUrlRu: deliverySettings.LauncherApiBaseUrlRu,
            LauncherApiBaseUrlEu: deliverySettings.LauncherApiBaseUrlEu,
            FallbackApiBaseUrls: deliverySettings.FallbackApiBaseUrls,
            Branding: branding,
            Constraints: new LauncherConstraints(
                ManagedLauncher: true,
                MinRamMb: ResolvePositiveInt(configuration["Launcher:MinRamMb"], 1024),
                ReservedSystemRamMb: ResolvePositiveInt(configuration["Launcher:ReservedSystemRamMb"], 1024),
                InstallTelemetryEnabled: installTelemetryEnabled,
                DiscordRpcEnabled: discordRpcSettings.Enabled,
                DiscordRpcPrivacyMode: discordRpcSettings.PrivacyMode),
            Profiles: profiles
                .Select(profile => new BootstrapProfileDto(
                    Id: profile.Id,
                    Name: profile.Name,
                    Slug: profile.Slug,
                    Description: profile.Description,
                    IconKey: profile.IconKey,
                    IconUrl: BuildPublicAssetPath(profile.IconKey),
                    Priority: profile.Priority,
                    RecommendedRamMb: profile.RecommendedRamMb,
                    BundledRuntimeKey: profile.BundledRuntimeKey,
                    DiscordRpc: profileDiscord.GetValueOrDefault(profile.Id),
                    Servers: profile.Servers
                        .OrderBy(server => server.Order)
                        .Select(server => new BootstrapServerDto(
                            Id: server.Id,
                            Name: server.Name,
                            Address: server.Address,
                            Port: server.Port,
                            MainJarPath: server.MainJarPath,
                            RuProxyAddress: server.RuProxyAddress,
                            RuProxyPort: server.RuProxyPort,
                            RuJarPath: server.RuJarPath,
                            IconKey: server.IconKey,
                            IconUrl: BuildPublicAssetPath(server.IconKey),
                            LoaderType: server.LoaderType,
                            McVersion: server.McVersion,
                            BuildId: server.BuildId,
                            DiscordRpc: serverDiscord.GetValueOrDefault(server.Id),
                            Order: server.Order))
                        .ToList()))
                .ToList(),
            News: news
                .Select(item => new BootstrapNewsItemDto(
                    Id: item.Id,
                    Title: item.Title,
                    Body: item.Body,
                    Source: item.Source,
                    ScopeType: NormalizeNewsScopeType(item.ScopeType),
                    ScopeId: NormalizeNewsScopeId(item.ScopeId),
                    ScopeName: ResolveNewsScopeName(item.ScopeType, item.ScopeId, profiles),
                    Pinned: item.Pinned,
                    CreatedAtUtc: item.CreatedAtUtc))
                .ToList(),
            LauncherUpdate: await ResolveLauncherUpdateAsync(cancellationToken));

        return Ok(response);
    }

    [HttpGet("news")]
    public async Task<ActionResult<IReadOnlyList<BootstrapNewsItemDto>>> News(CancellationToken cancellationToken)
    {
        var currentUsername = ResolveCurrentPlayerUsername();
        var profiles = await dbContext.Profiles
            .AsNoTracking()
            .Include(x => x.Servers.Where(server => server.Enabled))
            .Where(x => x.Enabled)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);
        profiles = profiles
            .Where(profile => ProfileAccessRules.CanAccess(profile, currentUsername))
            .ToList();

        var profileIds = profiles.Select(x => x.Id).ToHashSet();
        var serverIds = profiles.SelectMany(x => x.Servers).Select(x => x.Id).ToHashSet();

        var allNews = await dbContext.NewsItems
            .AsNoTracking()
            .Where(x => x.Enabled)
            .OrderByDescending(x => x.Pinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(60)
            .ToListAsync(cancellationToken);
        var news = FilterRelevantNews(allNews, profileIds, serverIds)
            .Take(20)
            .Select(item => new BootstrapNewsItemDto(
                item.Id,
                item.Title,
                item.Body,
                item.Source,
                NormalizeNewsScopeType(item.ScopeType),
                NormalizeNewsScopeId(item.ScopeId),
                ResolveNewsScopeName(item.ScopeType, item.ScopeId, profiles),
                item.Pinned,
                item.CreatedAtUtc))
            .ToList();

        return Ok(news);
    }

    [HttpGet("manifest/{profileSlug}")]
    public async Task<IActionResult> Manifest(string profileSlug, CancellationToken cancellationToken)
    {
        var normalizedSlug = profileSlug.Trim().ToLowerInvariant();
        var currentUsername = ResolveCurrentPlayerUsername();

        var profile = await dbContext.Profiles
            .AsNoTracking()
            .Where(x => x.Slug == normalizedSlug && x.Enabled)
            .Select(x => new
            {
                x.Id,
                x.Slug,
                x.LatestBuildId,
                x.LatestManifestKey,
                x.IsPrivate,
                x.AllowedPlayerUsernames
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return NotFound(new { error = "Profile not found." });
        }

        if (!ProfileAccessRules.CanAccess(
                profile.IsPrivate,
                profile.AllowedPlayerUsernames,
                currentUsername))
        {
            return NotFound(new { error = "Profile not found." });
        }

        var manifestKey = await ResolvePublishedManifestKeyAsync(
            profile.Id,
            profile.LatestManifestKey,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(manifestKey))
        {
            await EnsureInitialManifestBuildIfMissingAsync(profile.Id, profile.Slug, cancellationToken);
            manifestKey = await ResolvePublishedManifestKeyAsync(
                profile.Id,
                fallbackManifestKey: string.Empty,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(manifestKey))
        {
            return NotFound(new
            {
                error = "No manifest published for this profile. Upload files and run profile rebuild in admin."
            });
        }

        var storedObject = await objectStorageService.GetAsync(manifestKey, cancellationToken);
        if (storedObject is null)
        {
            return NotFound(new { error = "Manifest file not found in object storage." });
        }

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        if (!string.IsNullOrWhiteSpace(profile.LatestBuildId))
        {
            Response.Headers["X-Build-Id"] = profile.LatestBuildId.Trim();
        }

        var contentType = string.IsNullOrWhiteSpace(storedObject.ContentType)
            ? "application/json"
            : storedObject.ContentType;
        return File(storedObject.Data, contentType);
    }

    private async Task<string> ResolvePublishedManifestKeyAsync(
        Guid profileId,
        string? fallbackManifestKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(fallbackManifestKey))
        {
            return fallbackManifestKey.Trim();
        }

        return await dbContext.Builds
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId && x.Status == BuildStatus.Completed)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.ManifestKey)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
    }

    private async Task EnsureInitialManifestBuildIfMissingAsync(
        Guid profileId,
        string profileSlug,
        CancellationToken cancellationToken)
    {
        var locker = ManifestBuildLocks.GetOrAdd(profileId, static _ => new SemaphoreSlim(1, 1));
        await locker.WaitAsync(cancellationToken);
        try
        {
            var alreadyPublished = await dbContext.Builds
                .AsNoTracking()
                .AnyAsync(x => x.ProfileId == profileId && x.Status == BuildStatus.Completed, cancellationToken);
            if (alreadyPublished)
            {
                return;
            }

            var hasBuildInProgress = await dbContext.Builds
                .AsNoTracking()
                .AnyAsync(x => x.ProfileId == profileId && x.Status == BuildStatus.Building, cancellationToken);
            if (hasBuildInProgress)
            {
                return;
            }

            var latestFailedBuildAtUtc = await dbContext.Builds
                .AsNoTracking()
                .Where(x => x.ProfileId == profileId && x.Status == BuildStatus.Failed)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => (DateTime?)x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (latestFailedBuildAtUtc is DateTime failedAtUtc &&
                failedAtUtc >= DateTime.UtcNow.AddMinutes(-5))
            {
                return;
            }

            try
            {
                await buildPipelineService.RebuildProfileAsync(
                    profileId,
                    new ProfileRebuildRequest
                    {
                        PublishToServers = true
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Keep public endpoint stable; admin can inspect detailed rebuild failure in logs/audit.
                logger.LogWarning(
                    ex,
                    "Initial auto-build failed for profile '{ProfileSlug}' ({ProfileId}).",
                    profileSlug,
                    profileId);
            }
        }
        finally
        {
            locker.Release();
        }
    }

    [HttpGet("assets/{**key}")]
    public async Task<IActionResult> GetAsset(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return BadRequest(new { error = "Asset key is required." });
        }

        var normalizedKey = key.TrimStart('/');
        var storedObject = await objectStorageService.OpenReadAsync(normalizedKey, cancellationToken);
        if (storedObject is null)
        {
            return NotFound();
        }

        if (storedObject.SizeBytes is long sizeBytes)
        {
            Response.ContentLength = sizeBytes;
        }

        var result = File(storedObject.Stream, storedObject.ContentType);
        result.EnableRangeProcessing = true;
        return result;
    }

    private static int ResolvePositiveInt(string? rawValue, int fallback)
    {
        return int.TryParse(rawValue, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private string ResolvePublicBaseUrl(DeliverySettingsConfig settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
        {
            return settings.PublicBaseUrl.TrimEnd('/');
        }

        var requestBaseUrl = ResolveRequestPublicBaseUrl();
        if (!string.IsNullOrWhiteSpace(requestBaseUrl))
        {
            return requestBaseUrl;
        }

        var configured = (configuration["PUBLIC_BASE_URL"] ?? configuration["PublicBaseUrl"] ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? "http://localhost:8080"
            : configured.TrimEnd('/');
    }

    private static IEnumerable<NewsItem> FilterRelevantNews(
        IEnumerable<NewsItem> allNews,
        IReadOnlySet<Guid> profileIds,
        IReadOnlySet<Guid> serverIds)
    {
        return allNews.Where(item =>
        {
            var scopeType = NormalizeNewsScopeType(item.ScopeType);
            var scopeId = NormalizeNewsScopeId(item.ScopeId);
            if (scopeType == "global")
            {
                return true;
            }

            if (!Guid.TryParse(scopeId, out var parsedScopeId))
            {
                return false;
            }

            return scopeType == "profile"
                ? profileIds.Contains(parsedScopeId)
                : scopeType == "server" && serverIds.Contains(parsedScopeId);
        });
    }

    private static string NormalizeNewsScopeType(string? rawScopeType)
    {
        var normalized = (rawScopeType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "profile" => "profile",
            "server" => "server",
            _ => "global"
        };
    }

    private static string NormalizeNewsScopeId(string? rawScopeId)
    {
        return string.IsNullOrWhiteSpace(rawScopeId) ? string.Empty : rawScopeId.Trim();
    }

    private static string ResolveNewsScopeName(string? rawScopeType, string? rawScopeId, IReadOnlyList<Profile> profiles)
    {
        var scopeType = NormalizeNewsScopeType(rawScopeType);
        var scopeId = NormalizeNewsScopeId(rawScopeId);
        if (scopeType == "profile" && Guid.TryParse(scopeId, out var profileId))
        {
            return profiles.FirstOrDefault(x => x.Id == profileId)?.Name ?? "Profile";
        }

        if (scopeType == "server" && Guid.TryParse(scopeId, out var serverId))
        {
            return profiles
                .SelectMany(x => x.Servers)
                .FirstOrDefault(x => x.Id == serverId)
                ?.Name ?? "Server";
        }

        return "Global";
    }

    private string ResolveCurrentPlayerUsername()
    {
        if (User?.Identity?.IsAuthenticated != true)
        {
            return string.Empty;
        }

        var username = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name;
        return ProfileAccessRules.NormalizePlayerUsername(username);
    }

    private async Task<bool> ResolveInstallTelemetryEnabledAsync(CancellationToken cancellationToken)
    {
        var stored = await dbContext.InstallTelemetryConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => (bool?)x.Enabled)
            .FirstOrDefaultAsync(cancellationToken);

        return stored ?? installTelemetryOptions.Value.Enabled;
    }

    private async Task<(bool Enabled, bool PrivacyMode)> ResolveDiscordRpcSettingsAsync(CancellationToken cancellationToken)
    {
        var stored = await dbContext.DiscordRpcGlobalConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new { x.Enabled, x.PrivacyMode })
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is not null)
        {
            return (stored.Enabled, stored.PrivacyMode);
        }

        return (discordRpcOptions.Value.Enabled, discordRpcOptions.Value.PrivacyMode);
    }

    private static PublicDiscordRpcConfig MapDiscord(DiscordRpcConfig config, bool globallyEnabled, bool privacyMode)
    {
        var effectiveEnabled = globallyEnabled && config.Enabled;
        var details = privacyMode ? string.Empty : config.DetailsText;
        var state = privacyMode ? string.Empty : config.StateText;
        var largeText = privacyMode ? string.Empty : config.LargeImageText;
        var smallText = privacyMode ? string.Empty : config.SmallImageText;

        return new PublicDiscordRpcConfig(
            effectiveEnabled,
            config.AppId,
            details,
            state,
            config.LargeImageKey,
            largeText,
            config.SmallImageKey,
            smallText);
    }

    private async Task<LauncherUpdateInfo?> ResolveLauncherUpdateAsync(CancellationToken cancellationToken)
    {
        if (ShouldSuppressLauncherUpdateForCurrentClient())
        {
            return null;
        }

        var updateConfig = await launcherUpdateConfigProvider.GetAsync(cancellationToken);
        if (updateConfig is null)
        {
            return null;
        }

        return new LauncherUpdateInfo(
            LatestVersion: updateConfig.LatestVersion,
            DownloadUrl: updateConfig.DownloadUrl,
            ReleaseNotes: updateConfig.ReleaseNotes);
    }

    private BrandingConfig MapBranding(BrandingConfig branding)
    {
        var iconKey = (branding.LauncherIconKey ?? string.Empty).Trim();
        var iconUrl = string.IsNullOrWhiteSpace(iconKey)
            ? string.Empty
            : BuildPublicAssetPath(iconKey);
        var backgroundImageUrl = RewritePublicAssetUrlToRelativePublicAssetPath(branding.BackgroundImageUrl);

        return branding with
        {
            LauncherIconKey = iconKey,
            LauncherIconUrl = iconUrl,
            BackgroundImageUrl = backgroundImageUrl
        };
    }

    private string BuildRequestScopedAssetUrl(string? key)
    {
        var normalizedKey = (key ?? string.Empty).Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return string.Empty;
        }

        var relativeAssetPath = BuildPublicAssetPath(normalizedKey);
        if (string.IsNullOrWhiteSpace(relativeAssetPath))
        {
            return string.Empty;
        }

        var requestBaseUrl = ResolveRequestPublicBaseUrl();
        if (string.IsNullOrWhiteSpace(requestBaseUrl))
        {
            return assetUrlService.BuildPublicUrl(normalizedKey);
        }

        return $"{requestBaseUrl}{relativeAssetPath}";
    }

    private static string BuildPublicAssetPath(string? key)
    {
        var normalizedKey = (key ?? string.Empty).Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return string.Empty;
        }

        var escapedKey = string.Join('/',
            normalizedKey
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        return $"/api/public/assets/{escapedKey}";
    }

    private string RewritePublicAssetUrlToCurrentRequestHost(string? rawUrl)
    {
        var normalizedUrl = (rawUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUrl) ||
            !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var absoluteUri))
        {
            return normalizedUrl;
        }

        if (!absoluteUri.AbsolutePath.StartsWith("/api/public/assets/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedUrl;
        }

        var requestBaseUrl = ResolveRequestPublicBaseUrl();
        if (string.IsNullOrWhiteSpace(requestBaseUrl))
        {
            return normalizedUrl;
        }

        return $"{requestBaseUrl}{absoluteUri.PathAndQuery}";
    }

    private string RewritePublicAssetUrlToRelativePublicAssetPath(string? rawUrl)
    {
        var normalizedUrl = (rawUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.AbsolutePath.StartsWith("/api/public/assets/", StringComparison.OrdinalIgnoreCase)
                ? absoluteUri.PathAndQuery
                : normalizedUrl;
        }

        return normalizedUrl.StartsWith("/api/public/assets/", StringComparison.OrdinalIgnoreCase)
            ? normalizedUrl
            : normalizedUrl;
    }

    private bool ShouldSuppressLauncherUpdateForCurrentClient()
    {
        var versionText = ResolveLauncherClientVersionText();
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        if (!TryParseLauncherVersion(versionText, out var currentVersion))
        {
            return false;
        }

        return currentVersion <= new Version(1, 0, 3);
    }

    private string ResolveLauncherClientVersionText()
    {
        var explicitHeader = Request.Headers["X-BivLauncher-Client"].FirstOrDefault();
        var parsed = TryExtractLauncherVersion(explicitHeader);
        if (!string.IsNullOrWhiteSpace(parsed))
        {
            return parsed;
        }

        var userAgent = Request.Headers.UserAgent.ToString();
        return TryExtractLauncherVersion(userAgent);
    }

    private static string TryExtractLauncherVersion(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        const string marker = "BivLauncher.Client/";
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return string.Empty;
        }

        var versionStart = markerIndex + marker.Length;
        var tail = normalized[versionStart..].Trim();
        if (string.IsNullOrWhiteSpace(tail))
        {
            return string.Empty;
        }

        var separatorIndex = tail.IndexOfAny([' ', ';', ')', '(']);
        return separatorIndex >= 0 ? tail[..separatorIndex].Trim() : tail;
    }

    private static bool TryParseLauncherVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var separatorIndex = normalized.IndexOfAny(['-', '+']);
        if (separatorIndex >= 0)
        {
            normalized = normalized[..separatorIndex];
        }

        if (!Version.TryParse(normalized, out var parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    private string ResolveRequestPublicBaseUrl()
    {
        var forwardedProto = TryGetForwardedHeaderValue("X-Forwarded-Proto");
        var forwardedHost = TryGetForwardedHeaderValue("X-Forwarded-Host");
        var forwardedPrefix = TryGetForwardedHeaderValue("X-Forwarded-Prefix");

        var scheme = !string.IsNullOrWhiteSpace(forwardedProto)
            ? forwardedProto
            : Request.Scheme;
        var host = !string.IsNullOrWhiteSpace(forwardedHost)
            ? forwardedHost
            : Request.Host.Value;
        var pathBase = !string.IsNullOrWhiteSpace(forwardedPrefix)
            ? forwardedPrefix
            : Request.PathBase.Value;
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var normalizedPathBase = string.IsNullOrWhiteSpace(pathBase)
            ? string.Empty
            : "/" + pathBase.Trim().Trim('/');
        return $"{scheme}://{host}{normalizedPathBase}".TrimEnd('/');
    }

    private string TryGetForwardedHeaderValue(string headerName)
    {
        if (!Request.Headers.TryGetValue(headerName, out var values))
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
}
