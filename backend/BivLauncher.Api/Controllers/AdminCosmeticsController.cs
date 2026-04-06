using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin")]
public sealed class AdminCosmeticsController(
    IPlayerCosmeticsService playerCosmeticsService) : ControllerBase
{
    [HttpPost("skins/{user}/upload")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public Task<IActionResult> UploadSkin(string user, [FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        return UploadCosmeticAsync(user, "skin", file, cancellationToken);
    }

    [HttpPost("capes/{user}/upload")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public Task<IActionResult> UploadCape(string user, [FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        return UploadCosmeticAsync(user, "cape", file, cancellationToken);
    }

    private async Task<IActionResult> UploadCosmeticAsync(
        string user,
        string cosmeticType,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = User.Identity?.Name ?? "admin";
            var uploaded = await playerCosmeticsService.UploadAsync(
                user,
                cosmeticType,
                file,
                actor,
                source: "admin",
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
}
