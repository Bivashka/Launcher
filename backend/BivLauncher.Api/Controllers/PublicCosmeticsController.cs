using BivLauncher.Api.Data;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Route("api/public")]
public sealed class PublicCosmeticsController(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IAssetUrlService assetUrlService) : ControllerBase
{
    [HttpGet("skins/{user}")]
    [HttpGet("/skins/{user}.png")]
    public Task<IActionResult> GetSkin(string user, CancellationToken cancellationToken)
    {
        return GetCosmetic(user, "skin", cancellationToken);
    }

    [HttpGet("capes/{user}")]
    [HttpGet("/capes/{user}.png")]
    public Task<IActionResult> GetCape(string user, CancellationToken cancellationToken)
    {
        return GetCosmetic(user, "cape", cancellationToken);
    }

    [HttpGet("skins/{user}/meta")]
    public Task<IActionResult> GetSkinMeta(string user, CancellationToken cancellationToken)
    {
        return GetCosmeticMeta(user, "skin", cancellationToken);
    }

    [HttpGet("capes/{user}/meta")]
    public Task<IActionResult> GetCapeMeta(string user, CancellationToken cancellationToken)
    {
        return GetCosmeticMeta(user, "cape", cancellationToken);
    }

    private async Task<IActionResult> GetCosmetic(string user, string type, CancellationToken cancellationToken)
    {
        var key = await ResolveCosmeticKeyAsync(user, type, cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
        {
            return NotFound();
        }

        var storedObject = await objectStorageService.OpenReadAsync(key, cancellationToken);
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

    private async Task<IActionResult> GetCosmeticMeta(string user, string type, CancellationToken cancellationToken)
    {
        var key = await ResolveCosmeticKeyAsync(user, type, cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
        {
            return NotFound(new { error = "Cosmetic not found." });
        }

        return Ok(new
        {
            key,
            url = assetUrlService.BuildPublicUrl(key)
        });
    }

    private async Task<string> ResolveCosmeticKeyAsync(string user, string type, CancellationToken cancellationToken)
    {
        var normalized = user.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var accountId = await dbContext.AuthAccounts
            .AsNoTracking()
            .Where(x => x.Username == normalized || x.ExternalId == normalized)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (accountId == Guid.Empty)
        {
            return string.Empty;
        }

        if (type == "skin")
        {
            return await dbContext.SkinAssets
                .AsNoTracking()
                .Where(x => x.AccountId == accountId)
                .Select(x => x.Key)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        }

        return await dbContext.CapeAssets
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .Select(x => x.Key)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
    }
}
