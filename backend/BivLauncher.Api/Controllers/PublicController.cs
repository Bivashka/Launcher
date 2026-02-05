using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Route("api/public")]
public sealed class PublicController(
    AppDbContext dbContext,
    IBrandingProvider brandingProvider,
    IConfiguration configuration,
    IAssetUrlService assetUrlService,
    IObjectStorageService objectStorageService,
    IOptions<InstallTelemetryOptions> installTelemetryOptions,
    IOptions<DiscordRpcOptions> discordRpcOptions) : ControllerBase
{
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
            LauncherUpdate: ResolveLauncherUpdate(configuration));

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
            .Select(x => new { x.Id, x.LatestManifestKey })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return NotFound(new { error = "Profile not found." });
        }

        var manifestKey = profile.LatestManifestKey;
        if (string.IsNullOrWhiteSpace(manifestKey))
        {
            manifestKey = await dbContext.Builds
                .AsNoTracking()
                .Where(x => x.ProfileId == profile.Id && x.Status == BuildStatus.Completed)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => x.ManifestKey)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(manifestKey))
        {
            return NotFound(new { error = "No manifest published for this profile." });
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

    private static LauncherUpdateInfo? ResolveLauncherUpdate(IConfiguration configuration)
    {
        var latestVersion = (configuration["LAUNCHER_LATEST_VERSION"]
                             ?? configuration["LauncherUpdate:LatestVersion"]
                             ?? string.Empty).Trim();
        var downloadUrl = (configuration["LAUNCHER_UPDATE_URL"]
                           ?? configuration["LauncherUpdate:DownloadUrl"]
                           ?? string.Empty).Trim();
        var releaseNotes = (configuration["LAUNCHER_RELEASE_NOTES"]
                            ?? configuration["LauncherUpdate:ReleaseNotes"]
                            ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(latestVersion) || string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        return new LauncherUpdateInfo(
            LatestVersion: latestVersion,
            DownloadUrl: downloadUrl,
            ReleaseNotes: releaseNotes);
    }
}
