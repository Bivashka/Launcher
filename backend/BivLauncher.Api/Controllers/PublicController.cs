using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
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
    IAssetUrlService assetUrlService,
    IObjectStorageService objectStorageService,
    IOptions<InstallTelemetryOptions> installTelemetryOptions,
    IOptions<DiscordRpcOptions> discordRpcOptions,
    ILogger<PublicController> logger) : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ManifestBuildLocks = new();

    [HttpGet("bootstrap")]
    public async Task<ActionResult<BootstrapResponse>> Bootstrap(CancellationToken cancellationToken)
    {
        var profiles = await dbContext.Profiles
            .AsNoTracking()
            .Include(x => x.Servers.Where(server => server.Enabled))
            .Where(x => x.Enabled)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);

        var profileIds = profiles.Select(x => x.Id).ToHashSet();
        var serverIds = profiles.SelectMany(x => x.Servers).Select(x => x.Id).ToHashSet();

        var discordConfigs = await dbContext.DiscordRpcConfigs
            .AsNoTracking()
            .Where(x =>
                (x.ScopeType == "profile" && profileIds.Contains(x.ScopeId)) ||
                (x.ScopeType == "server" && serverIds.Contains(x.ScopeId)))
            .ToListAsync(cancellationToken);

        var news = await dbContext.NewsItems
            .AsNoTracking()
            .Where(x => x.Enabled)
            .OrderByDescending(x => x.Pinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        var branding = await brandingProvider.GetBrandingAsync(cancellationToken);
        var publicBaseUrl = configuration["PUBLIC_BASE_URL"]
            ?? configuration["PublicBaseUrl"]
            ?? "http://localhost:8080";
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
                    IconUrl: assetUrlService.BuildPublicUrl(profile.IconKey),
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
                            IconUrl: assetUrlService.BuildPublicUrl(server.IconKey),
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
                    Pinned: item.Pinned,
                    CreatedAtUtc: item.CreatedAtUtc))
                .ToList(),
            LauncherUpdate: await ResolveLauncherUpdateAsync(cancellationToken));

        return Ok(response);
    }

    [HttpGet("news")]
    public async Task<ActionResult<IReadOnlyList<BootstrapNewsItemDto>>> News(CancellationToken cancellationToken)
    {
        var news = await dbContext.NewsItems
            .AsNoTracking()
            .Where(x => x.Enabled)
            .OrderByDescending(x => x.Pinned)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(20)
            .Select(item => new BootstrapNewsItemDto(
                item.Id,
                item.Title,
                item.Body,
                item.Source,
                item.Pinned,
                item.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(news);
    }

    [HttpGet("manifest/{profileSlug}")]
    public async Task<IActionResult> Manifest(string profileSlug, CancellationToken cancellationToken)
    {
        var normalizedSlug = profileSlug.Trim().ToLowerInvariant();

        var profile = await dbContext.Profiles
            .AsNoTracking()
            .Where(x => x.Slug == normalizedSlug && x.Enabled)
            .Select(x => new { x.Id, x.Slug, x.LatestManifestKey })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
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

        var manifest = JsonSerializer.Deserialize<LauncherManifest>(storedObject.Data, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return manifest is null
            ? StatusCode(StatusCodes.Status500InternalServerError, new { error = "Manifest content is invalid." })
            : Ok(manifest);
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

            var hasAnyBuild = await dbContext.Builds
                .AsNoTracking()
                .AnyAsync(x => x.ProfileId == profileId, cancellationToken);
            if (hasAnyBuild)
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
        var storedObject = await objectStorageService.GetAsync(normalizedKey, cancellationToken);
        if (storedObject is null)
        {
            return NotFound();
        }

        return File(storedObject.Data, storedObject.ContentType);
    }

    private static int ResolvePositiveInt(string? rawValue, int fallback)
    {
        return int.TryParse(rawValue, out var parsed) && parsed > 0 ? parsed : fallback;
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
}
