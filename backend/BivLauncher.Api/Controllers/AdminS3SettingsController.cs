using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings/s3")]
public sealed class AdminS3SettingsController(
    AppDbContext dbContext,
    IOptions<S3Options> fallbackOptions,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<S3SettingsDto>> Get(CancellationToken cancellationToken)
    {
        var stored = await dbContext.S3StorageConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            var fallback = fallbackOptions.Value;
            return Ok(new S3SettingsDto(
                fallback.Endpoint,
                fallback.Bucket,
                fallback.AccessKey,
                fallback.SecretKey,
                fallback.ForcePathStyle,
                fallback.UseSsl,
                fallback.AutoCreateBucket,
                null));
        }

        return Ok(Map(stored));
    }

    [HttpPut]
    public async Task<ActionResult<S3SettingsDto>> Put(
        [FromBody] S3SettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = request.Endpoint.Trim();
        var bucket = request.Bucket.Trim();
        var accessKey = request.AccessKey.Trim();
        var secretKey = request.SecretKey.Trim();

        if (bucket.Contains('/') || bucket.Contains('\\'))
        {
            return BadRequest(new { error = "S3 bucket name must not contain '/' or '\\'." });
        }

        var config = await dbContext.S3StorageConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new S3StorageConfig();
            dbContext.S3StorageConfigs.Add(config);
        }

        config.Endpoint = endpoint;
        config.Bucket = bucket;
        config.AccessKey = accessKey;
        config.SecretKey = secretKey;
        config.ForcePathStyle = request.ForcePathStyle;
        config.UseSsl = request.UseSsl;
        config.AutoCreateBucket = request.AutoCreateBucket;
        config.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "settings.s3.update",
            actor: actor,
            entityType: "settings",
            entityId: "s3",
            details: new
            {
                config.Endpoint,
                config.Bucket,
                config.ForcePathStyle,
                config.UseSsl,
                config.AutoCreateBucket,
                hasAccessKey = !string.IsNullOrWhiteSpace(config.AccessKey),
                hasSecretKey = !string.IsNullOrWhiteSpace(config.SecretKey)
            },
            cancellationToken: cancellationToken);

        return Ok(Map(config));
    }

    private static S3SettingsDto Map(S3StorageConfig config)
    {
        return new S3SettingsDto(
            config.Endpoint,
            config.Bucket,
            config.AccessKey,
            config.SecretKey,
            config.ForcePathStyle,
            config.UseSsl,
            config.AutoCreateBucket,
            config.UpdatedAtUtc);
    }
}
