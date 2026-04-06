using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using BivLauncher.Api.Services;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Route("api/site")]
public sealed class SiteCosmeticsController(
    ISecuritySettingsProvider securitySettingsProvider,
    IPlayerCosmeticsService playerCosmeticsService) : ControllerBase
{
    private const string SiteCosmeticsUploadSecretHeaderName = "X-Site-Cosmetics-Upload-Secret";

    [HttpPost("skins/{user}/upload")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public Task<IActionResult> UploadSkin(
        string user,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        return UploadCosmeticAsync(user, "skin", file, cancellationToken);
    }

    [HttpPost("capes/{user}/upload")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public Task<IActionResult> UploadCape(
        string user,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        return UploadCosmeticAsync(user, "cape", file, cancellationToken);
    }

    private async Task<IActionResult> UploadCosmeticAsync(
        string user,
        string cosmeticType,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var secretError = ValidateSiteUploadSecret();
        if (secretError is not null)
        {
            return secretError;
        }

        try
        {
            var uploaded = await playerCosmeticsService.UploadAsync(
                user,
                cosmeticType,
                file,
                actor: "site",
                source: "site",
                cancellationToken);
            return Ok(uploaded);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    private IActionResult? ValidateSiteUploadSecret()
    {
        var requiredSecret = (securitySettingsProvider.GetCachedSettings().SiteCosmeticsUploadSecret ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(requiredSecret))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Site cosmetics upload is not configured."
            });
        }

        var providedSecret = Request.Headers[SiteCosmeticsUploadSecretHeaderName].ToString().Trim();
        if (string.IsNullOrWhiteSpace(providedSecret))
        {
            return Unauthorized(new { error = "Missing site cosmetics upload secret." });
        }

        var requiredBytes = Encoding.UTF8.GetBytes(requiredSecret);
        var providedBytes = Encoding.UTF8.GetBytes(providedSecret);
        if (requiredBytes.Length != providedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(requiredBytes, providedBytes))
        {
            return Unauthorized(new { error = "Invalid site cosmetics upload secret." });
        }

        return null;
    }
}
