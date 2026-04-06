using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BivLauncher.Api.Services;

public sealed class PlayerCosmeticsService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IAssetUrlService assetUrlService,
    IAdminAuditService auditService,
    ILogger<PlayerCosmeticsService> logger) : IPlayerCosmeticsService
{
    private const long MaxUploadBytes = 8 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".gif",
        ".svg"
    ];

    public async Task<PlayerCosmeticUploadResult> UploadAsync(
        string user,
        string cosmeticType,
        IFormFile? file,
        string actor,
        string source,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            throw new InvalidOperationException("File is required.");
        }

        if (file.Length > MaxUploadBytes)
        {
            throw new InvalidOperationException("File is too large. Max size is 8 MB.");
        }

        var normalizedType = (cosmeticType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedType is not ("skin" or "cape"))
        {
            throw new InvalidOperationException("Unsupported cosmetic type.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Unsupported image extension.");
        }

        var normalizedUser = (user ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedUser))
        {
            throw new InvalidOperationException("User is required.");
        }

        var account = await dbContext.AuthAccounts.FirstOrDefaultAsync(
            x => x.Username == normalizedUser || x.ExternalId == normalizedUser,
            cancellationToken);

        if (account is null)
        {
            throw new KeyNotFoundException("Account not found.");
        }

        var key = normalizedType == "skin"
            ? $"skins/{account.Id}/skin{extension}"
            : $"capes/{account.Id}/cape{extension}";

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        await using (var stream = file.OpenReadStream())
        {
            await objectStorageService.UploadAsync(key, stream, contentType, cancellationToken: cancellationToken);
        }

        string? previousKey;
        if (normalizedType == "skin")
        {
            var existing = await dbContext.SkinAssets.FirstOrDefaultAsync(x => x.AccountId == account.Id, cancellationToken);
            previousKey = existing?.Key;
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
            previousKey = existing?.Key;
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

        if (!string.IsNullOrWhiteSpace(previousKey) &&
            !string.Equals(previousKey, key, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await objectStorageService.DeleteAsync(previousKey, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to delete previous cosmetic object '{PreviousKey}' after uploading '{Key}'.",
                    previousKey,
                    key);
            }
        }

        await auditService.WriteAsync(
            action: normalizedType == "skin" ? "cosmetics.skin.upload" : "cosmetics.cape.upload",
            actor: string.IsNullOrWhiteSpace(actor) ? source : actor,
            entityType: "account",
            entityId: account.ExternalId,
            details: new
            {
                accountId = account.Id,
                account.Username,
                key,
                previousKey,
                fileName = file.FileName,
                sizeBytes = file.Length,
                source = string.IsNullOrWhiteSpace(source) ? "unknown" : source
            },
            cancellationToken: cancellationToken);

        return new PlayerCosmeticUploadResult(
            Account: account.Username,
            Key: key,
            Url: assetUrlService.BuildPublicUrl(key));
    }
}
