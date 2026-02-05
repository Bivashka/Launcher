using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class StorageMigrationService(
    IHostEnvironment hostEnvironment,
    ILogger<StorageMigrationService> logger) : IStorageMigrationService
{
    private static readonly JsonSerializerOptions LocalMetadataJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHostEnvironment _hostEnvironment = hostEnvironment;
    private readonly ILogger<StorageMigrationService> _logger = logger;

    public async Task<StorageMigrationResult> MigrateAsync(
        StorageConnectionSettings source,
        StorageConnectionSettings target,
        StorageMigrationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(source);
        ValidateSettings(target);

        var normalizedPrefix = NormalizePrefix(options.Prefix);
        var maxObjects = Math.Clamp(options.MaxObjects, 1, 500000);
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        AmazonS3Client? sourceS3 = null;
        AmazonS3Client? targetS3 = null;
        try
        {
            sourceS3 = source.UseS3 ? CreateS3Client(source) : null;
            targetS3 = target.UseS3 ? CreateS3Client(target) : null;

            if (source.UseS3)
            {
                await EnsureBucketReadyAsync(sourceS3!, source, createIfMissing: false, cancellationToken);
            }

            if (target.UseS3)
            {
                await EnsureBucketReadyAsync(targetS3!, target, createIfMissing: target.AutoCreateBucket, cancellationToken);
            }

            await ProbeTargetWriteAsync(target, targetS3, cancellationToken);

            var listedKeys = await ListKeysAsync(source, sourceS3, normalizedPrefix, maxObjects + 1, cancellationToken);
            var truncated = listedKeys.Count > maxObjects;
            var keys = truncated ? listedKeys.Take(maxObjects).ToList() : listedKeys;

            var copied = 0;
            var skipped = 0;
            var failed = 0;
            long copiedBytes = 0;
            var errors = new List<string>();

            foreach (var key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!options.Overwrite)
                    {
                        var exists = await ExistsAsync(target, targetS3, key, cancellationToken);
                        if (exists)
                        {
                            skipped++;
                            continue;
                        }
                    }

                    var item = await ReadObjectAsync(source, sourceS3, key, cancellationToken);
                    if (item is null)
                    {
                        failed++;
                        AppendError(errors, $"Missing source object: {key}");
                        continue;
                    }

                    copiedBytes += item.Data.LongLength;
                    if (!options.DryRun)
                    {
                        await WriteObjectAsync(target, targetS3, key, item, cancellationToken);
                    }

                    copied++;
                }
                catch (Exception ex)
                {
                    failed++;
                    AppendError(errors, $"{key}: {ex.Message}");
                }
            }

            var finishedAtUtc = DateTime.UtcNow;
            var result = new StorageMigrationResult(
                DryRun: options.DryRun,
                SourceUseS3: source.UseS3,
                TargetUseS3: target.UseS3,
                Scanned: keys.Count,
                Copied: copied,
                Skipped: skipped,
                Failed: failed,
                CopiedBytes: copiedBytes,
                Truncated: truncated,
                DurationMs: stopwatch.ElapsedMilliseconds,
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: finishedAtUtc,
                Errors: errors);

            _logger.LogInformation(
                "Storage migration done: dryRun={DryRun}, sourceUseS3={SourceUseS3}, targetUseS3={TargetUseS3}, scanned={Scanned}, copied={Copied}, skipped={Skipped}, failed={Failed}, truncated={Truncated}",
                result.DryRun,
                result.SourceUseS3,
                result.TargetUseS3,
                result.Scanned,
                result.Copied,
                result.Skipped,
                result.Failed,
                result.Truncated);

            return result;
        }
        finally
        {
            sourceS3?.Dispose();
            targetS3?.Dispose();
        }
    }

    private async Task ProbeTargetWriteAsync(
        StorageConnectionSettings target,
        AmazonS3Client? targetS3,
        CancellationToken cancellationToken)
    {
        var probeKey = $"_healthchecks/storage-migration/{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.probe";
        var payload = Encoding.UTF8.GetBytes("migration_probe");
        var item = new MigrationObject(
            Data: payload,
            ContentType: "text/plain; charset=utf-8",
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        try
        {
            await WriteObjectAsync(target, targetS3, probeKey, item, cancellationToken);
            var downloaded = await ReadObjectAsync(target, targetS3, probeKey, cancellationToken);
            if (downloaded is null || !payload.SequenceEqual(downloaded.Data))
            {
                throw new InvalidOperationException("Probe read/write check failed.");
            }
        }
        finally
        {
            await DeleteIfExistsAsync(target, targetS3, probeKey, cancellationToken);
        }
    }

    private async Task DeleteIfExistsAsync(
        StorageConnectionSettings settings,
        AmazonS3Client? s3Client,
        string key,
        CancellationToken cancellationToken)
    {
        if (settings.UseS3)
        {
            try
            {
                await s3Client!.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = settings.Bucket,
                    Key = NormalizeStorageKey(key)
                }, cancellationToken);
            }
            catch
            {
            }

            return;
        }

        var objectPath = ResolveLocalObjectPath(settings, key, createParentDirectory: false);
        if (File.Exists(objectPath))
        {
            File.Delete(objectPath);
        }

        var metadataPath = ResolveLocalMetadataPath(objectPath);
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }
    }

    private async Task<IReadOnlyList<string>> ListKeysAsync(
        StorageConnectionSettings settings,
        AmazonS3Client? s3Client,
        string prefix,
        int limit,
        CancellationToken cancellationToken)
    {
        return settings.UseS3
            ? await ListS3KeysAsync(settings, s3Client!, prefix, limit, cancellationToken)
            : ListLocalKeys(settings, prefix, limit);
    }

    private static async Task<IReadOnlyList<string>> ListS3KeysAsync(
        StorageConnectionSettings settings,
        AmazonS3Client s3Client,
        string prefix,
        int limit,
        CancellationToken cancellationToken)
    {
        var results = new List<string>(Math.Min(limit, 20000));
        string? continuationToken = null;
        do
        {
            var response = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = settings.Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            }, cancellationToken);

            foreach (var obj in response.S3Objects)
            {
                results.Add(obj.Key);
                if (results.Count >= limit)
                {
                    return results;
                }
            }

            continuationToken = response.IsTruncated ? response.NextContinuationToken : null;
        } while (!string.IsNullOrWhiteSpace(continuationToken));

        return results;
    }

    private IReadOnlyList<string> ListLocalKeys(
        StorageConnectionSettings settings,
        string prefix,
        int limit)
    {
        var rootPath = ResolveLocalRootPath(settings, ensureExists: false);
        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        var results = new List<string>(Math.Min(limit, 20000));
        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            if (path.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(prefix) &&
                !relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(relative);
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private async Task<bool> ExistsAsync(
        StorageConnectionSettings settings,
        AmazonS3Client? s3Client,
        string key,
        CancellationToken cancellationToken)
    {
        if (settings.UseS3)
        {
            try
            {
                await s3Client!.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = settings.Bucket,
                    Key = NormalizeStorageKey(key)
                }, cancellationToken);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
            {
                return false;
            }
        }

        var objectPath = ResolveLocalObjectPath(settings, key, createParentDirectory: false);
        return File.Exists(objectPath);
    }

    private async Task<MigrationObject?> ReadObjectAsync(
        StorageConnectionSettings settings,
        AmazonS3Client? s3Client,
        string key,
        CancellationToken cancellationToken)
    {
        return settings.UseS3
            ? await ReadS3ObjectAsync(settings, s3Client!, key, cancellationToken)
            : await ReadLocalObjectAsync(settings, key, cancellationToken);
    }

    private static async Task<MigrationObject?> ReadS3ObjectAsync(
        StorageConnectionSettings settings,
        AmazonS3Client s3Client,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await s3Client.GetObjectAsync(settings.Bucket, NormalizeStorageKey(key), cancellationToken);
            await using var responseStream = response.ResponseStream;
            using var ms = new MemoryStream();
            await responseStream.CopyToAsync(ms, cancellationToken);

            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;
            var metadata = ParseS3Metadata(response.Metadata);

            return new MigrationObject(ms.ToArray(), contentType, metadata);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    private async Task<MigrationObject?> ReadLocalObjectAsync(
        StorageConnectionSettings settings,
        string key,
        CancellationToken cancellationToken)
    {
        var objectPath = ResolveLocalObjectPath(settings, key, createParentDirectory: false);
        if (!File.Exists(objectPath))
        {
            return null;
        }

        var data = await File.ReadAllBytesAsync(objectPath, cancellationToken);
        var metadataDoc = await TryReadLocalMetadataAsync(objectPath, cancellationToken);
        var contentType = string.IsNullOrWhiteSpace(metadataDoc?.ContentType)
            ? InferContentTypeFromKey(key)
            : metadataDoc.ContentType!.Trim();
        var metadata = metadataDoc?.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadataDoc.Metadata, StringComparer.OrdinalIgnoreCase);

        if (!metadata.ContainsKey("sha256"))
        {
            metadata["sha256"] = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        return new MigrationObject(data, contentType, metadata);
    }

    private async Task WriteObjectAsync(
        StorageConnectionSettings settings,
        AmazonS3Client? s3Client,
        string key,
        MigrationObject item,
        CancellationToken cancellationToken)
    {
        if (settings.UseS3)
        {
            var request = new PutObjectRequest
            {
                BucketName = settings.Bucket,
                Key = NormalizeStorageKey(key),
                InputStream = new MemoryStream(item.Data, writable: false),
                AutoCloseStream = true,
                ContentType = item.ContentType
            };

            foreach (var pair in item.Metadata)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                request.Metadata[pair.Key.Trim()] = pair.Value.Trim();
            }

            await s3Client!.PutObjectAsync(request, cancellationToken);
            return;
        }

        var objectPath = ResolveLocalObjectPath(settings, key, createParentDirectory: true);
        await File.WriteAllBytesAsync(objectPath, item.Data, cancellationToken);

        var metadataDoc = new LocalObjectMetadataDocument
        {
            ContentType = item.ContentType,
            Metadata = item.Metadata
        };
        await WriteLocalMetadataAsync(objectPath, metadataDoc, cancellationToken);
    }

    private static Dictionary<string, string> ParseS3Metadata(MetadataCollection metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var metadataKey in metadata.Keys)
        {
            var raw = metadata[metadataKey];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var key = metadataKey;
            if (key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase))
            {
                key = key["x-amz-meta-".Length..];
            }

            key = key.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            result[key] = raw.Trim();
        }

        return result;
    }

    private static AmazonS3Client CreateS3Client(StorageConnectionSettings settings)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = settings.Endpoint,
            ForcePathStyle = settings.ForcePathStyle,
            UseHttp = !settings.UseSsl,
            AuthenticationRegion = "us-east-1"
        };

        return new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
            config);
    }

    private static async Task EnsureBucketReadyAsync(
        IAmazonS3 client,
        StorageConnectionSettings settings,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var exists = await AmazonS3Util.DoesS3BucketExistV2Async(client, settings.Bucket);
        if (exists)
        {
            return;
        }

        if (!createIfMissing)
        {
            throw new InvalidOperationException($"S3 bucket '{settings.Bucket}' does not exist.");
        }

        await client.PutBucketAsync(new PutBucketRequest { BucketName = settings.Bucket }, cancellationToken);
    }

    private static void ValidateSettings(StorageConnectionSettings settings)
    {
        if (!settings.UseS3)
        {
            if (string.IsNullOrWhiteSpace(settings.LocalRootPath))
            {
                throw new InvalidOperationException("Local storage root path is not configured.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new InvalidOperationException("S3 endpoint is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.Bucket))
        {
            throw new InvalidOperationException("S3 bucket is not configured.");
        }

        if (settings.Bucket.Contains('/') || settings.Bucket.Contains('\\'))
        {
            throw new InvalidOperationException("S3 bucket name must not contain '/' or '\\'.");
        }

        if (string.IsNullOrWhiteSpace(settings.AccessKey) || string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvalidOperationException("S3 credentials are not configured.");
        }
    }

    private string ResolveLocalRootPath(StorageConnectionSettings settings, bool ensureExists)
    {
        var localRootPath = settings.LocalRootPath.Trim();
        var absolutePath = Path.IsPathRooted(localRootPath)
            ? Path.GetFullPath(localRootPath)
            : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, localRootPath));

        if (ensureExists)
        {
            Directory.CreateDirectory(absolutePath);
        }

        return absolutePath;
    }

    private string ResolveLocalObjectPath(StorageConnectionSettings settings, string key, bool createParentDirectory)
    {
        var normalizedKey = NormalizeStorageKey(key);
        var rootPath = ResolveLocalRootPath(settings, ensureExists: createParentDirectory);
        var relativePath = normalizedKey.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

        if (!IsSubPath(rootPath, fullPath))
        {
            throw new InvalidOperationException("Storage key escapes configured local storage root path.");
        }

        if (createParentDirectory)
        {
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        return fullPath;
    }

    private static bool IsSubPath(string rootPath, string candidatePath)
    {
        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveLocalMetadataPath(string objectPath)
    {
        return $"{objectPath}.meta.json";
    }

    private static async Task<LocalObjectMetadataDocument?> TryReadLocalMetadataAsync(
        string objectPath,
        CancellationToken cancellationToken)
    {
        var metadataPath = ResolveLocalMetadataPath(objectPath);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                metadataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            return await JsonSerializer.DeserializeAsync<LocalObjectMetadataDocument>(
                stream,
                LocalMetadataJsonOptions,
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteLocalMetadataAsync(
        string objectPath,
        LocalObjectMetadataDocument document,
        CancellationToken cancellationToken)
    {
        var metadataPath = ResolveLocalMetadataPath(objectPath);
        var tempPath = $"{metadataPath}.tmp";

        await using (var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, document, LocalMetadataJsonOptions, cancellationToken);
        }

        File.Move(tempPath, metadataPath, overwrite: true);
    }

    private static string NormalizeStorageKey(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            throw new InvalidOperationException("Storage key is required.");
        }

        var cleaned = rawKey.Trim().Replace('\\', '/').TrimStart('/');
        var parts = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new InvalidOperationException("Storage key is invalid.");
        }

        foreach (var part in parts)
        {
            if (part is "." or "..")
            {
                throw new InvalidOperationException("Storage key contains invalid path traversal segments.");
            }
        }

        return string.Join('/', parts);
    }

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        return NormalizeStorageKey(prefix);
    }

    private static string InferContentTypeFromKey(string key)
    {
        var extension = Path.GetExtension(key).ToLowerInvariant();
        return extension switch
        {
            ".json" => "application/json",
            ".txt" => "text/plain; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".zip" => "application/zip",
            ".jar" => "application/java-archive",
            _ => "application/octet-stream"
        };
    }

    private static void AppendError(List<string> errors, string message)
    {
        if (errors.Count >= 100)
        {
            return;
        }

        errors.Add(message);
    }

    private sealed record MigrationObject(
        byte[] Data,
        string ContentType,
        Dictionary<string, string> Metadata);

    private sealed class LocalObjectMetadataDocument
    {
        public string ContentType { get; set; } = "application/octet-stream";
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
