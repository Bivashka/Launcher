using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings/delivery")]
public sealed class AdminDeliverySettingsController(
    IDeliverySettingsProvider deliverySettingsProvider,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DeliverySettingsDto>> Get(CancellationToken cancellationToken)
    {
        var settings = await deliverySettingsProvider.GetSettingsAsync(cancellationToken);
        return Ok(Map(settings));
    }

    [HttpPut]
    public async Task<ActionResult<DeliverySettingsDto>> Put(
        [FromBody] DeliverySettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await deliverySettingsProvider.SaveSettingsAsync(
            new DeliverySettingsConfig(
                PublicBaseUrl: request.PublicBaseUrl,
                AssetBaseUrl: request.AssetBaseUrl,
                FallbackApiBaseUrls: request.FallbackApiBaseUrls,
                UpdatedAtUtc: DateTime.UtcNow),
            cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.delivery.update",
            actor: actor,
            entityType: "settings",
            entityId: "delivery",
            details: new
            {
                saved.PublicBaseUrl,
                saved.AssetBaseUrl,
                fallbackApiBaseUrls = saved.FallbackApiBaseUrls
            },
            cancellationToken: cancellationToken);

        return Ok(Map(saved));
    }

    private static DeliverySettingsDto Map(DeliverySettingsConfig config)
    {
        return new DeliverySettingsDto(
            config.PublicBaseUrl,
            config.AssetBaseUrl,
            config.FallbackApiBaseUrls,
            config.UpdatedAtUtc);
    }
}
