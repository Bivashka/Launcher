using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/settings/s3")]
public sealed class AdminS3SettingsController(
    AppDbContext dbContext,
    IOptions<S3Options> fallbackOptions,
    IObjectStorageService objectStorageService,
    IStorageMigrationService storageMigrationService,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<S3SettingsDto>> Get(CancellationToken cancellationToken)
    {
        var effective = await ResolveEffectiveSettingsAsync(cancellationToken);
        return Ok(effective);
    }

    [HttpPut]
    public async Task<ActionResult<S3SettingsDto>> Put(
        [FromBody] S3SettingsUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var localRootPath = request.LocalRootPath.Trim();
        var endpoint = request.Endpoint.Trim();
        var bucket = request.Bucket.Trim();
        var accessKey = request.AccessKey.Trim();
        var secretKey = request.SecretKey.Trim();

        if (!request.UseS3 && string.IsNullOrWhiteSpace(localRootPath))
        {
            return BadRequest(new { error = "Local storage root path is required when S3 mode is disabled." });
        }

        if (request.UseS3 && string.IsNullOrWhiteSpace(endpoint))
        {
            return BadRequest(new { error = "S3 endpoint is required in S3 mode." });
        }

        if (request.UseS3 && string.IsNullOrWhiteSpace(bucket))
        {
            return BadRequest(new { error = "S3 bucket is required in S3 mode." });
        }

        if (request.UseS3 && (bucket.Contains('/') || bucket.Contains('\\')))
        {
            return BadRequest(new { error = "S3 bucket name must not contain '/' or '\\'." });
        }

        if (request.UseS3 && (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey)))
        {
            return BadRequest(new { error = "S3 access key and secret key are required in S3 mode." });
        }

        var config = await dbContext.S3StorageConfigs
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (config is null)
        {
            config = new S3StorageConfig();
            dbContext.S3StorageConfigs.Add(config);
        }

        config.UseS3 = request.UseS3;
        config.LocalRootPath = string.IsNullOrWhiteSpace(localRootPath) ? "Storage" : localRootPath;
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
                config.UseS3,
                config.LocalRootPath,
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

    [HttpPost("test")]
    public async Task<ActionResult<StorageTestResultDto>> TestStorage(CancellationToken cancellationToken)
    {
        var effective = await ResolveEffectiveSettingsAsync(cancellationToken);
        var useS3 = effective.UseS3;
        var startedAt = Stopwatch.GetTimestamp();
        var testedAtUtc = DateTime.UtcNow;
        var probeKey = $"_healthchecks/storage/{testedAtUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}.probe";
        var payload = Encoding.UTF8.GetBytes($"storage_probe:{testedAtUtc:O}");

        try
        {
            await using (var uploadStream = new MemoryStream(payload, writable: false))
            {
                await objectStorageService.UploadAsync(
                    probeKey,
                    uploadStream,
                    "text/plain; charset=utf-8",
                    cancellationToken: cancellationToken);
            }

            var downloaded = await objectStorageService.GetAsync(probeKey, cancellationToken);
            if (downloaded is null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new StorageTestResultDto(
                    Success: false,
                    UseS3: useS3,
                    Message: "Storage probe upload succeeded but object cannot be downloaded.",
                    ProbeKey: probeKey,
                    RoundTripMs: (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                    TestedAtUtc: testedAtUtc));
            }

            if (!payload.SequenceEqual(downloaded.Data))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new StorageTestResultDto(
                    Success: false,
                    UseS3: useS3,
                    Message: "Storage probe mismatch: uploaded and downloaded payload differ.",
                    ProbeKey: probeKey,
                    RoundTripMs: (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                    TestedAtUtc: testedAtUtc));
            }

            await objectStorageService.DeleteAsync(probeKey, cancellationToken);

            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "settings.s3.test",
                actor: actor,
                entityType: "settings",
                entityId: "s3",
                details: new
                {
                    success = true,
                    useS3,
                    probeKey,
                    elapsedMs
                },
                cancellationToken: cancellationToken);

            return Ok(new StorageTestResultDto(
                Success: true,
                UseS3: useS3,
                Message: "Storage probe passed (upload, download and delete).",
                ProbeKey: probeKey,
                RoundTripMs: elapsedMs,
                TestedAtUtc: testedAtUtc));
        }
        catch (Exception ex)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "settings.s3.test.failed",
                actor: actor,
                entityType: "settings",
                entityId: "s3",
                details: new
                {
                    success = false,
                    useS3,
                    probeKey,
                    elapsedMs,
                    error = ex.Message
                },
                cancellationToken: cancellationToken);

            return StatusCode(StatusCodes.Status500InternalServerError, new StorageTestResultDto(
                Success: false,
                UseS3: useS3,
                Message: $"Storage probe failed: {ex.Message}",
                ProbeKey: probeKey,
                RoundTripMs: elapsedMs,
                TestedAtUtc: testedAtUtc));
        }
        finally
        {
            try
            {
                await objectStorageService.DeleteAsync(probeKey, cancellationToken);
            }
            catch
            {
            }
        }
    }

    [HttpPost("migrate")]
    public async Task<ActionResult<StorageMigrationResultDto>> Migrate(
        [FromBody] StorageMigrationRequest request,
        CancellationToken cancellationToken)
    {
        var effective = await ResolveEffectiveSettingsAsync(cancellationToken);
        var source = ToConnectionSettings(effective);

        if (source.UseS3 == request.TargetUseS3)
        {
            return BadRequest(new { error = "Current storage mode is already selected as migration target." });
        }

        var target = source with
        {
            UseS3 = request.TargetUseS3
        };

        var result = await storageMigrationService.MigrateAsync(
            source,
            target,
            new StorageMigrationOptions(
                DryRun: request.DryRun,
                Overwrite: request.Overwrite,
                MaxObjects: request.MaxObjects,
                Prefix: request.Prefix),
            cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: request.DryRun ? "settings.s3.migrate.dry-run" : "settings.s3.migrate",
            actor: actor,
            entityType: "settings",
            entityId: "s3",
            details: new
            {
                result.DryRun,
                result.SourceUseS3,
                result.TargetUseS3,
                result.Scanned,
                result.Copied,
                result.Skipped,
                result.Failed,
                result.CopiedBytes,
                result.Truncated,
                result.DurationMs
            },
            cancellationToken: cancellationToken);

        return Ok(new StorageMigrationResultDto(
            result.DryRun,
            result.SourceUseS3,
            result.TargetUseS3,
            result.Scanned,
            result.Copied,
            result.Skipped,
            result.Failed,
            result.CopiedBytes,
            result.Truncated,
            result.DurationMs,
            result.StartedAtUtc,
            result.FinishedAtUtc,
            result.Errors));
    }

    private static S3SettingsDto Map(S3StorageConfig config)
    {
        return new S3SettingsDto(
            config.UseS3,
            config.LocalRootPath,
            config.Endpoint,
            config.Bucket,
            config.AccessKey,
            config.SecretKey,
            config.ForcePathStyle,
            config.UseSsl,
            config.AutoCreateBucket,
            config.UpdatedAtUtc);
    }

    private async Task<S3SettingsDto> ResolveEffectiveSettingsAsync(CancellationToken cancellationToken)
    {
        var stored = await dbContext.S3StorageConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            var fallback = fallbackOptions.Value;
            return new S3SettingsDto(
                fallback.UseS3,
                fallback.LocalRootPath,
                fallback.Endpoint,
                fallback.Bucket,
                fallback.AccessKey,
                fallback.SecretKey,
                fallback.ForcePathStyle,
                fallback.UseSsl,
                fallback.AutoCreateBucket,
                null);
        }

        return Map(stored);
    }

    private static StorageConnectionSettings ToConnectionSettings(S3SettingsDto settings)
    {
        return new StorageConnectionSettings(
            settings.UseS3,
            settings.LocalRootPath,
            settings.Endpoint,
            settings.Bucket,
            settings.AccessKey,
            settings.SecretKey,
            settings.ForcePathStyle,
            settings.UseSsl,
            settings.AutoCreateBucket);
    }
}
