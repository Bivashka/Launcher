using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin")]
public sealed class AdminUploadController(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IAssetUrlService assetUrlService,
    IAdminAuditService auditService) : ControllerBase
{
    private static readonly HashSet<string> AllowedCategories = ["profiles", "servers", "assets", "runtimes", "news", "skins", "capes"];
    private static readonly HashSet<string> AllowedImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".svg"];

    [HttpPost("upload")]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    public async Task<IActionResult> Upload(
        [FromQuery] string category = "assets",
        [FromQuery] string? entityId = null,
        [FromForm] IFormFile? file = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return BadRequest(new { error = "File is required." });
        }

        var normalizedCategory = category.Trim().ToLowerInvariant();
        if (!AllowedCategories.Contains(normalizedCategory))
        {
            return BadRequest(new { error = $"Unsupported category '{category}'." });
        }

        var maxSizeBytes = normalizedCategory == "runtimes"
            ? 1024L * 1024L * 1024L
            : 15L * 1024L * 1024L;

        if (file.Length > maxSizeBytes)
        {
            var limitMb = maxSizeBytes / (1024L * 1024L);
            return BadRequest(new { error = $"File is too large. Max size is {limitMb} MB." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        if ((normalizedCategory is "profiles" or "servers") && !AllowedImageExtensions.Contains(extension))
        {
            return BadRequest(new { error = "Profile and server icons must be image files." });
        }

        if (normalizedCategory == "runtimes" && string.IsNullOrWhiteSpace(entityId))
        {
            return BadRequest(new { error = "Runtime upload requires entityId (profile slug)." });
        }

        string runtimeSha256 = string.Empty;
        long runtimeSizeBytes = 0;
        string runtimeContentType = string.Empty;

        Profile? linkedProfile = null;
        Guid? linkedProfileId = null;
        string linkedProfileSlug = string.Empty;
        if (normalizedCategory == "runtimes")
        {
            var profileSlug = NormalizeSegment(entityId).ToLowerInvariant();
            linkedProfile = await dbContext.Profiles
                .FirstOrDefaultAsync(x => x.Slug == profileSlug, cancellationToken);
            if (linkedProfile is null)
            {
                return BadRequest(new { error = $"Profile '{profileSlug}' not found for runtime upload." });
            }

            linkedProfileId = linkedProfile.Id;
            linkedProfileSlug = linkedProfile.Slug;
            runtimeSha256 = await ComputeSha256HexAsync(file, cancellationToken);
            runtimeSizeBytes = file.Length;
            runtimeContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        }

        var key = BuildKey(normalizedCategory, entityId, extension);
        IReadOnlyDictionary<string, string>? uploadMetadata = null;
        if (normalizedCategory == "runtimes" && !string.IsNullOrWhiteSpace(runtimeSha256))
        {
            uploadMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sha256"] = runtimeSha256
            };
        }

        await using var stream = file.OpenReadStream();
        await objectStorageService.UploadAsync(key, stream, file.ContentType, uploadMetadata, cancellationToken);

        if (normalizedCategory == "runtimes")
        {
            if (linkedProfile is null)
            {
                return BadRequest(new { error = "Runtime profile link is missing." });
            }

            linkedProfile.BundledRuntimeKey = key;
            linkedProfile.BundledRuntimeSha256 = runtimeSha256;
            linkedProfile.BundledRuntimeSizeBytes = runtimeSizeBytes;
            linkedProfile.BundledRuntimeContentType = runtimeContentType;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (normalizedCategory == "runtimes")
        {
            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "runtime.upload",
                actor: actor,
                entityType: "profile",
                entityId: linkedProfileSlug,
                details: new
                {
                    key,
                    fileName = file.FileName,
                    sizeBytes = file.Length,
                    contentType = runtimeContentType,
                    sha256 = runtimeSha256
                },
                cancellationToken: cancellationToken);
        }
        else
        {
            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: $"{normalizedCategory}.upload",
                actor: actor,
                entityType: normalizedCategory,
                entityId: NormalizeSegment(entityId),
                details: new
                {
                    key,
                    fileName = file.FileName,
                    sizeBytes = file.Length,
                    contentType = file.ContentType
                },
                cancellationToken: cancellationToken);
        }

        return Ok(new
        {
            key,
            publicUrl = assetUrlService.BuildPublicUrl(key),
            size = file.Length,
            contentType = file.ContentType,
            linkedProfileId,
            linkedProfileSlug,
            runtimeSha256,
            runtimeSizeBytes,
            runtimeContentType
        });
    }

    [HttpGet("runtimes/verify")]
    public async Task<IActionResult> VerifyRuntimeArtifact(
        [FromQuery] string? entityId = null,
        [FromQuery] string? key = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeStorageKey(key);

        Profile? profile = null;
        if (!string.IsNullOrWhiteSpace(entityId))
        {
            var profileSlug = NormalizeSegment(entityId).ToLowerInvariant();
            profile = await dbContext.Profiles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Slug == profileSlug, cancellationToken);
            if (profile is null)
            {
                return BadRequest(new { error = $"Profile '{profileSlug}' not found." });
            }
        }

        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            var profileKey = profile?.BundledRuntimeKey ?? string.Empty;
            normalizedKey = NormalizeStorageKey(profileKey);
        }

        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return BadRequest(new { error = "Runtime key is required (query key or profile entityId with bundled runtime key)." });
        }

        var metadata = await objectStorageService.GetMetadataAsync(normalizedKey, cancellationToken);
        if (metadata is null)
        {
            return NotFound(new { error = $"Runtime artifact '{normalizedKey}' not found in object storage." });
        }

        var profileRuntimeSha256 = profile?.BundledRuntimeSha256 ?? string.Empty;
        var profileRuntimeSizeBytes = profile?.BundledRuntimeSizeBytes ?? 0;
        var profileRuntimeContentType = profile?.BundledRuntimeContentType ?? string.Empty;
        var sha256MatchesProfile = string.IsNullOrWhiteSpace(profileRuntimeSha256) ||
                                   string.Equals(profileRuntimeSha256, metadata.Sha256, StringComparison.OrdinalIgnoreCase);
        var sizeMatchesProfile = profileRuntimeSizeBytes <= 0 || profileRuntimeSizeBytes == metadata.SizeBytes;
        var contentTypeMatchesProfile = string.IsNullOrWhiteSpace(profileRuntimeContentType) ||
                                        string.Equals(profileRuntimeContentType, metadata.ContentType, StringComparison.OrdinalIgnoreCase);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "runtime.verify",
            actor: actor,
            entityType: profile is null ? "runtime" : "profile",
            entityId: profile?.Slug ?? normalizedKey,
            details: new
            {
                key = normalizedKey,
                resolvedFromProfile = string.IsNullOrWhiteSpace(key),
                linkedProfileSlug = profile?.Slug ?? string.Empty,
                sha256MatchesProfile,
                sizeMatchesProfile,
                contentTypeMatchesProfile
            },
            cancellationToken: cancellationToken);

        return Ok(new
        {
            key = normalizedKey,
            resolvedFromProfile = string.IsNullOrWhiteSpace(key),
            linkedProfileId = profile?.Id,
            linkedProfileSlug = profile?.Slug ?? string.Empty,
            profileRuntimeKey = profile?.BundledRuntimeKey ?? string.Empty,
            profileRuntimeSha256,
            profileRuntimeSizeBytes,
            profileRuntimeContentType,
            storageSha256 = metadata.Sha256,
            storageSizeBytes = metadata.SizeBytes,
            storageContentType = metadata.ContentType,
            sha256MatchesProfile,
            sizeMatchesProfile,
            contentTypeMatchesProfile
        });
    }

    [HttpPost("runtimes/cleanup")]
    public async Task<IActionResult> CleanupRuntimeArtifacts(
        [FromQuery] string? entityId = null,
        [FromQuery] int keepLast = 3,
        [FromQuery] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        var profileSlug = NormalizeSegment(entityId).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(profileSlug))
        {
            return BadRequest(new { error = "Runtime cleanup requires entityId (profile slug)." });
        }

        if (keepLast < 0 || keepLast > 100)
        {
            return BadRequest(new { error = "keepLast must be between 0 and 100." });
        }

        var profile = await dbContext.Profiles
            .FirstOrDefaultAsync(x => x.Slug == profileSlug, cancellationToken);
        if (profile is null)
        {
            return BadRequest(new { error = $"Profile '{profileSlug}' not found." });
        }

        var runtimePrefix = $"runtimes/{profileSlug}/";
        var runtimeObjects = await objectStorageService.ListByPrefixAsync(runtimePrefix, cancellationToken);
        var orderedKeys = runtimeObjects
            .OrderByDescending(x => x.LastModifiedUtc)
            .ThenByDescending(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => NormalizeStorageKey(x.Key))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keepKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profileRuntimeKey = NormalizeStorageKey(profile.BundledRuntimeKey);
        if (!string.IsNullOrWhiteSpace(profileRuntimeKey))
        {
            keepKeys.Add(profileRuntimeKey);
        }

        foreach (var key in orderedKeys)
        {
            if (keepKeys.Count >= keepLast + (string.IsNullOrWhiteSpace(profileRuntimeKey) ? 0 : 1))
            {
                break;
            }

            keepKeys.Add(key);
        }

        var deleteKeys = orderedKeys
            .Where(x => !keepKeys.Contains(x))
            .ToList();

        if (!dryRun)
        {
            foreach (var deleteKey in deleteKeys)
            {
                await objectStorageService.DeleteAsync(deleteKey, cancellationToken);
            }
        }

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "runtime.cleanup",
            actor: actor,
            entityType: "profile",
            entityId: profile.Slug,
            details: new
            {
                dryRun,
                keepLast,
                totalFound = orderedKeys.Count,
                keepCount = keepKeys.Count,
                deleteCount = deleteKeys.Count,
                deletedCount = dryRun ? 0 : deleteKeys.Count
            },
            cancellationToken: cancellationToken);

        return Ok(new
        {
            profileId = profile.Id,
            profileSlug = profile.Slug,
            dryRun,
            keepLast,
            totalFound = orderedKeys.Count,
            keepKeys = keepKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            deleteKeys = deleteKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            deletedCount = dryRun ? 0 : deleteKeys.Count
        });
    }

    private static async Task<string> ComputeSha256HexAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildKey(string category, string? entityId, string extension)
    {
        var normalizedEntity = NormalizeSegment(entityId);

        if (category is "profiles" or "servers")
        {
            if (!string.IsNullOrWhiteSpace(normalizedEntity))
            {
                return $"icons/{category}/{normalizedEntity}{extension}";
            }

            return $"uploads/{category}/{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        }

        if (category == "runtimes")
        {
            var runtimeOwner = string.IsNullOrWhiteSpace(normalizedEntity) ? "unknown-profile" : normalizedEntity;
            return $"runtimes/{runtimeOwner}/{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        }

        if (category == "assets" && !string.IsNullOrWhiteSpace(normalizedEntity))
        {
            return $"uploads/{category}/{normalizedEntity}{extension}";
        }

        return $"uploads/{category}/{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
    }

    private static string NormalizeStorageKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key)
            ? string.Empty
            : key.Trim().Replace('\\', '/').TrimStart('/');
    }

    private static string NormalizeSegment(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim();
        var chars = trimmed
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'
                ? ch
                : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-');
        return normalized.Length == 0 ? string.Empty : normalized;
    }
}
