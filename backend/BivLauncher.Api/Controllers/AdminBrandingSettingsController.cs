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
    private static readonly HashSet<string> AllowedImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];

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
                hasBackgroundImage = !string.IsNullOrWhiteSpace(saved.BackgroundImageUrl),
                saved.PrimaryColor,
                saved.AccentColor
            },
            cancellationToken: cancellationToken);
        return Ok(Map(saved));
    }

    [HttpPost("background")]
    [RequestSizeLimit(20L * 1024L * 1024L)]
    public async Task<ActionResult<BrandingSettingsDto>> UploadBackgroundImage(
        [FromForm] IFormFile? file = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return BadRequest(new { error = "Background image file is required." });
        }

        if (file.Length > 20L * 1024L * 1024L)
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

    private static string BuildBackgroundStorageKey(string extension)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"branding/background/{timestamp}_{suffix}{extension}";
    }

    private static BrandingSettingsDto Map(BrandingConfig branding)
    {
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
            branding.BackgroundImageUrl,
            branding.BackgroundOverlayOpacity,
            branding.LoginCardPosition,
            branding.LoginCardWidth);
    }
}
