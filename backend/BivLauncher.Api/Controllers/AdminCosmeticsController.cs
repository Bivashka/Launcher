using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin")]
public sealed class AdminCosmeticsController(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IAssetUrlService assetUrlService,
    IAdminAuditService auditService) : ControllerBase
{
    private static readonly HashSet<string> AllowedImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg"];

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
        if (file is null || file.Length <= 0)
        {
            return BadRequest(new { error = "File is required." });
        }

        if (file.Length > 8 * 1024 * 1024)
        {
            return BadRequest(new { error = "File is too large. Max size is 8 MB." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
        {
            return BadRequest(new { error = "Unsupported image extension." });
        }

        var normalizedUser = user.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            return BadRequest(new { error = "User is required." });
        }

        var account = await dbContext.AuthAccounts.FirstOrDefaultAsync(
            x => x.Username == normalizedUser || x.ExternalId == normalizedUser,
            cancellationToken);

        if (account is null)
        {
            return NotFound(new { error = "Account not found." });
        }

        var key = cosmeticType == "skin"
            ? $"skins/{account.Id}/skin{extension}"
            : $"capes/{account.Id}/cape{extension}";

        await using var stream = file.OpenReadStream();
        await objectStorageService.UploadAsync(key, stream, file.ContentType, cancellationToken: cancellationToken);

        if (cosmeticType == "skin")
        {
            var existing = await dbContext.SkinAssets.FirstOrDefaultAsync(x => x.AccountId == account.Id, cancellationToken);
            if (existing is null)
            {
                dbContext.SkinAssets.Add(new SkinAsset
                {
                    AccountId = account.Id,
                    Key = key
                });
            }
            else
            {
                existing.Key = key;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        else
        {
            var existing = await dbContext.CapeAssets.FirstOrDefaultAsync(x => x.AccountId == account.Id, cancellationToken);
            if (existing is null)
            {
                dbContext.CapeAssets.Add(new CapeAsset
                {
                    AccountId = account.Id,
                    Key = key
                });
            }
            else
            {
                existing.Key = key;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: cosmeticType == "skin" ? "cosmetics.skin.upload" : "cosmetics.cape.upload",
            actor: actor,
            entityType: "account",
            entityId: account.ExternalId,
            details: new
            {
                accountId = account.Id,
                account.Username,
                key,
                fileName = file.FileName,
                sizeBytes = file.Length
            },
            cancellationToken: cancellationToken);

        return Ok(new
        {
            account = account.Username,
            key,
            url = assetUrlService.BuildPublicUrl(key)
        });
    }
}
