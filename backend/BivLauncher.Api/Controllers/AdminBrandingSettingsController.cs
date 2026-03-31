using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings/branding")]
public sealed class AdminBrandingSettingsController(
    IBrandingProvider brandingProvider,
    IObjectStorageService objectStorageService,
    IAssetUrlService assetUrlService,
    IAdminAuditService auditService,
    ILogger<AdminBrandingSettingsController> logger) : ControllerBase
{
    private const long MaxBackgroundImageBytes = 20L * 1024L * 1024L;
    private const long MaxLauncherIconBytes = 2L * 1024L * 1024L;
    private static readonly HashSet<string> AllowedImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];
    private static readonly HashSet<string> AllowedLauncherIconExtensions = [".ico"];

    [HttpGet]
    public async Task<ActionResult<BrandingSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var branding = await brandingProvider.GetBrandingAsync(cancellationToken);
        return Ok(Map(branding));
    }

    [HttpPut]
    public async Task<ActionResult<BrandingSettingsDto>> Put(
        [FromBody] BrandingSettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var launcherIconKey = request.LauncherIconKey.Trim();
        var launcherIconUrl = string.IsNullOrWhiteSpace(launcherIconKey)
            ? string.Empty
            : assetUrlService.BuildPublicUrl(launcherIconKey);

        var branding = new BrandingConfig(
            ProductName: request.ProductName.Trim(),
            LauncherDirectoryName: request.LauncherDirectoryName.Trim(),
            DeveloperName: request.DeveloperName.Trim(),
            Tagline: request.Tagline.Trim(),
            SupportUrl: request.SupportUrl.Trim(),
            PrimaryColor: request.PrimaryColor.Trim(),
            AccentColor: request.AccentColor.Trim(),
            SurfaceColor: request.SurfaceColor.Trim(),
            SurfaceBorderColor: request.SurfaceBorderColor.Trim(),
            TextPrimaryColor: request.TextPrimaryColor.Trim(),
            TextSecondaryColor: request.TextSecondaryColor.Trim(),
            PrimaryButtonColor: request.PrimaryButtonColor.Trim(),
            PrimaryButtonBorderColor: request.PrimaryButtonBorderColor.Trim(),
            PrimaryButtonTextColor: request.PrimaryButtonTextColor.Trim(),
            PlayButtonColor: request.PlayButtonColor.Trim(),
            PlayButtonBorderColor: request.PlayButtonBorderColor.Trim(),
            PlayButtonTextColor: request.PlayButtonTextColor.Trim(),
            InputBackgroundColor: request.InputBackgroundColor.Trim(),
            InputBorderColor: request.InputBorderColor.Trim(),
            InputTextColor: request.InputTextColor.Trim(),
            ListBackgroundColor: request.ListBackgroundColor.Trim(),
            ListBorderColor: request.ListBorderColor.Trim(),
            LogoText: request.LogoText.Trim(),
            LauncherIconKey: launcherIconKey,
            LauncherIconUrl: launcherIconUrl,
            BackgroundImageUrl: request.BackgroundImageUrl.Trim(),
            BackgroundOverlayOpacity: request.BackgroundOverlayOpacity,
            LoginCardPosition: request.LoginCardPosition.Trim(),
            LoginCardWidth: request.LoginCardWidth);

        var saved = await brandingProvider.SaveBrandingAsync(branding, cancellationToken);
        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.branding.update",
            actor: actor,
            entityType: "settings",
            entityId: "branding",
            details: new
            {
                saved.ProductName,
                saved.LauncherDirectoryName,
                saved.DeveloperName,
                saved.LogoText,
                saved.LoginCardPosition,
                saved.LoginCardWidth,
                hasLauncherIcon = !string.IsNullOrWhiteSpace(saved.LauncherIconKey),
                hasBackgroundImage = !string.IsNullOrWhiteSpace(saved.BackgroundImageUrl),
                saved.PrimaryColor,
                saved.AccentColor
            },
            cancellationToken: cancellationToken);
        return Ok(Map(saved));
    }

    [HttpPost("background")]
    [RequestSizeLimit(MaxBackgroundImageBytes)]
    public async Task<ActionResult<BrandingSettingsDto>> UploadBackgroundImage(
        [FromForm] IFormFile? file = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return BadRequest(new { error = "Background image file is required." });
        }

        if (file.Length > MaxBackgroundImageBytes)
        {
            return BadRequest(new { error = "Background image is too large. Max size is 20 MB." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
        {
            return BadRequest(new { error = "Unsupported image format. Allowed: .png, .jpg, .jpeg, .webp, .gif" });
        }

        var key = BuildBackgroundStorageKey(extension);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType.Trim();

        try
        {
            await using (var stream = file.OpenReadStream())
            {
                await objectStorageService.UploadAsync(key, stream, contentType, cancellationToken: cancellationToken);
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Branding background upload rejected due to storage configuration.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = $"Storage backend is unavailable: {ex.Message}"
            });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Branding background upload failed due to storage connectivity issue.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Storage backend is unreachable. Check S3/MinIO endpoint and connectivity."
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Branding background upload failed unexpectedly.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Background image upload failed unexpectedly."
            });
        }

        var current = await brandingProvider.GetBrandingAsync(cancellationToken);
        var updated = current with
        {
            BackgroundImageUrl = assetUrlService.BuildPublicUrl(key)
        };
        var saved = await brandingProvider.SaveBrandingAsync(updated, cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.branding.background.upload",
            actor: actor,
            entityType: "settings",
            entityId: "branding",
            details: new
            {
                key,
                fileName = file.FileName,
                fileSizeBytes = file.Length,
                contentType
            },
            cancellationToken: cancellationToken);

        return Ok(Map(saved));
    }

    [HttpPost("icon")]
    [RequestSizeLimit(MaxLauncherIconBytes)]
    public async Task<ActionResult<BrandingSettingsDto>> UploadLauncherIcon(
        [FromForm] IFormFile? file = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return BadRequest(new { error = "Launcher icon file is required." });
        }

        if (file.Length > MaxLauncherIconBytes)
        {
            return BadRequest(new { error = "Launcher icon is too large. Max size is 2 MB." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedLauncherIconExtensions.Contains(extension))
        {
            return BadRequest(new { error = "Unsupported icon format. Allowed: .ico" });
        }

        var key = BuildLauncherIconStorageKey(extension);
        const string contentType = "image/x-icon";

        try
        {
            await using (var stream = file.OpenReadStream())
            {
                await objectStorageService.UploadAsync(key, stream, contentType, cancellationToken: cancellationToken);
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Branding launcher icon upload rejected due to storage configuration.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = $"Storage backend is unavailable: {ex.Message}"
            });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Branding launcher icon upload failed due to storage connectivity issue.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Storage backend is unreachable. Check S3/MinIO endpoint and connectivity."
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Branding launcher icon upload failed unexpectedly.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Launcher icon upload failed unexpectedly."
            });
        }

        var current = await brandingProvider.GetBrandingAsync(cancellationToken);
        var updated = current with
        {
            LauncherIconKey = key,
            LauncherIconUrl = assetUrlService.BuildPublicUrl(key)
        };
        var saved = await brandingProvider.SaveBrandingAsync(updated, cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.branding.icon.upload",
            actor: actor,
            entityType: "settings",
            entityId: "branding",
            details: new
            {
                key,
                fileName = file.FileName,
                fileSizeBytes = file.Length,
                contentType
            },
            cancellationToken: cancellationToken);

        return Ok(Map(saved));
    }

    private static string BuildBackgroundStorageKey(string extension)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"branding/background/{timestamp}_{suffix}{extension}";
    }

    private static string BuildLauncherIconStorageKey(string extension)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"branding/icon/{timestamp}_{suffix}{extension}";
    }

    private BrandingSettingsDto Map(BrandingConfig branding)
    {
        var launcherIconKey = (branding.LauncherIconKey ?? string.Empty).Trim();
        var launcherIconUrl = string.IsNullOrWhiteSpace(launcherIconKey)
            ? string.Empty
            : assetUrlService.BuildPublicUrl(launcherIconKey);

        return new BrandingSettingsDto(
            branding.ProductName,
            branding.LauncherDirectoryName,
            branding.DeveloperName,
            branding.Tagline,
            branding.SupportUrl,
            branding.PrimaryColor,
            branding.AccentColor,
            branding.SurfaceColor,
            branding.SurfaceBorderColor,
            branding.TextPrimaryColor,
            branding.TextSecondaryColor,
            branding.PrimaryButtonColor,
            branding.PrimaryButtonBorderColor,
            branding.PrimaryButtonTextColor,
            branding.PlayButtonColor,
            branding.PlayButtonBorderColor,
            branding.PlayButtonTextColor,
            branding.InputBackgroundColor,
            branding.InputBorderColor,
            branding.InputTextColor,
            branding.ListBackgroundColor,
            branding.ListBorderColor,
            branding.LogoText,
            launcherIconKey,
            launcherIconUrl,
            branding.BackgroundImageUrl,
            branding.BackgroundOverlayOpacity,
            branding.LoginCardPosition,
            branding.LoginCardWidth);
    }
}
