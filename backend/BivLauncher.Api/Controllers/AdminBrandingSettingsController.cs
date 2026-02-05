using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings/branding")]
public sealed class AdminBrandingSettingsController(
    IBrandingProvider brandingProvider,
    IAdminAuditService auditService) : ControllerBase
{
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
            DeveloperName: request.DeveloperName.Trim(),
            Tagline: request.Tagline.Trim(),
            SupportUrl: request.SupportUrl.Trim(),
            PrimaryColor: request.PrimaryColor.Trim(),
            AccentColor: request.AccentColor.Trim(),
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
                saved.DeveloperName,
                saved.LogoText,
                saved.LoginCardPosition,
                saved.LoginCardWidth,
                hasBackgroundImage = !string.IsNullOrWhiteSpace(saved.BackgroundImageUrl)
            },
            cancellationToken: cancellationToken);
        return Ok(Map(saved));
    }

    private static BrandingSettingsDto Map(BrandingConfig branding)
    {
        return new BrandingSettingsDto(
            branding.ProductName,
            branding.DeveloperName,
            branding.Tagline,
            branding.SupportUrl,
            branding.PrimaryColor,
            branding.AccentColor,
            branding.LogoText,
            branding.BackgroundImageUrl,
            branding.BackgroundOverlayOpacity,
            branding.LoginCardPosition,
            branding.LoginCardWidth);
    }
}
