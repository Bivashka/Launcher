using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace BivLauncher.Api.Services;

public sealed class S3ObjectStorageService(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<S3Options> fallbackOptions,
    ILogger<S3ObjectStorageService> logger) : IObjectStorageService
{
    private static readonly TimeSpan SettingsCacheDuration = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly S3Options _fallbackOptions = fallbackOptions.Value;
    private readonly ILogger<S3ObjectStorageService> _logger = logger;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private readonly SemaphoreSlim _bucketLock = new(1, 1);

    private ResolvedS3Settings? _cachedSettings;
    private DateTime _cachedSettingsAtUtc = DateTime.MinValue;
    private AmazonS3Client? _client;
    private string _clientSignature = string.Empty;
    private string _bucketSignature = string.Empty;

    public async Task UploadAsync(
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        var client = await GetClientAsync(settings, cancellationToken);
        await EnsureBucketExistsAsync(client, settings, cancellationToken);

        var request = new PutObjectRequest
        {
            BucketName = settings.Bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        };

        if (metadata is not null)
        {
            foreach (var pair in metadata)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                request.Metadata[pair.Key.Trim()] = pair.Value.Trim();
            }
        }

        await client.PutObjectAsync(request, cancellationToken);
    }

    public async Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        var client = await GetClientAsync(settings, cancellationToken);
        await EnsureBucketExistsAsync(client, settings, cancellationToken);

        try
        {
            var response = await client.GetObjectAsync(settings.Bucket, key, cancellationToken);
            await using var responseStream = response.ResponseStream;
            using var ms = new MemoryStream();
            await responseStream.CopyToAsync(ms, cancellationToken);

            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;

            return new StoredObject(ms.ToArray(), contentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    public async Task<StoredObjectMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        var client = await GetClientAsync(settings, cancellationToken);
        await EnsureBucketExistsAsync(client, settings, cancellationToken);

        try
        {
            var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = settings.Bucket,
                Key = key
            }, cancellationToken);

            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;
            var sizeBytes = response.Headers.ContentLength;
            var sha256 = ResolveSha256(response.Metadata);

            return new StoredObjectMetadata(sizeBytes, contentType, sha256);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<StoredObjectListItem>> ListByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        var client = await GetClientAsync(settings, cancellationToken);
        await EnsureBucketExistsAsync(client, settings, cancellationToken);

        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : prefix.Trim().Replace('\\', '/').TrimStart('/');

        var results = new List<StoredObjectListItem>();
        string? continuationToken = null;
        do
        {
            var response = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = settings.Bucket,
                Prefix = normalizedPrefix,
                ContinuationToken = continuationToken
            }, cancellationToken);

            results.AddRange(response.S3Objects.Select(obj => new StoredObjectListItem(
                obj.Key,
                obj.Size,
                obj.LastModified.ToUniversalTime())));

            continuationToken = response.IsTruncated ? response.NextContinuationToken : null;
        } while (!string.IsNullOrWhiteSpace(continuationToken));

        return results;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        var client = await GetClientAsync(settings, cancellationToken);
        await EnsureBucketExistsAsync(client, settings, cancellationToken);

        await client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = settings.Bucket,
            Key = key
        }, cancellationToken);
    }

    private async Task<ResolvedS3Settings> ResolveSettingsAsync(CancellationToken cancellationToken)
    {
        if (_cachedSettings is not null &&
            DateTime.UtcNow - _cachedSettingsAtUtc <= SettingsCacheDuration)
        {
            return _cachedSettings;
        }

        await _settingsLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedSettings is not null &&
                DateTime.UtcNow - _cachedSettingsAtUtc <= SettingsCacheDuration)
            {
                return _cachedSettings;
            }

            var stored = await LoadStoredSettingsAsync(cancellationToken);
            var resolved = stored is null
                ? ResolveFromFallback(_fallbackOptions)
                : ResolveFromStorage(stored);

            ValidateSettings(resolved);

            _cachedSettings = resolved;
            _cachedSettingsAtUtc = DateTime.UtcNow;
            return resolved;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private async Task<S3StorageConfig?> LoadStoredSettingsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.S3StorageConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<AmazonS3Client> GetClientAsync(ResolvedS3Settings settings, CancellationToken cancellationToken)
    {
        var nextSignature = BuildClientSignature(settings);
        if (_client is not null && string.Equals(_clientSignature, nextSignature, StringComparison.Ordinal))
        {
            return _client;
        }

        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null && string.Equals(_clientSignature, nextSignature, StringComparison.Ordinal))
            {
                return _client;
            }

            var config = new AmazonS3Config
            {
                ServiceURL = settings.Endpoint,
                ForcePathStyle = settings.ForcePathStyle,
                UseHttp = !settings.UseSsl,
                AuthenticationRegion = "us-east-1"
            };

            _client?.Dispose();
            _client = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config);
            _clientSignature = nextSignature;
            _bucketSignature = string.Empty;

            _logger.LogInformation("Using S3 endpoint {Endpoint} with bucket {Bucket}", settings.Endpoint, settings.Bucket);
            return _client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private static string BuildClientSignature(ResolvedS3Settings settings)
    {
        return string.Join('|',
            settings.Endpoint,
            settings.AccessKey,
            settings.SecretKey,
            settings.ForcePathStyle,
            settings.UseSsl);
    }

    private async Task EnsureBucketExistsAsync(
        IAmazonS3 client,
        ResolvedS3Settings settings,
        CancellationToken cancellationToken)
    {
        var nextBucketSignature = $"{_clientSignature}|{settings.Bucket}|{settings.AutoCreateBucket}";
        if (string.Equals(_bucketSignature, nextBucketSignature, StringComparison.Ordinal))
        {
            return;
        }

        await _bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_bucketSignature, nextBucketSignature, StringComparison.Ordinal))
            {
                return;
            }

            var exists = await AmazonS3Util.DoesS3BucketExistV2Async(client, settings.Bucket);
            if (!exists)
            {
                if (!settings.AutoCreateBucket)
                {
                    throw new InvalidOperationException($"S3 bucket '{settings.Bucket}' does not exist.");
                }

                _logger.LogInformation("Creating missing S3 bucket {BucketName}", settings.Bucket);
                await client.PutBucketAsync(new PutBucketRequest { BucketName = settings.Bucket }, cancellationToken);
            }

            _bucketSignature = nextBucketSignature;
        }
        finally
        {
            _bucketLock.Release();
        }
    }

    private static ResolvedS3Settings ResolveFromStorage(S3StorageConfig config)
    {
        return new ResolvedS3Settings(
            Endpoint: config.Endpoint.Trim(),
            Bucket: config.Bucket.Trim(),
            AccessKey: config.AccessKey.Trim(),
            SecretKey: config.SecretKey.Trim(),
            ForcePathStyle: config.ForcePathStyle,
            UseSsl: config.UseSsl,
            AutoCreateBucket: config.AutoCreateBucket);
    }

    private static ResolvedS3Settings ResolveFromFallback(S3Options options)
    {
        return new ResolvedS3Settings(
            Endpoint: options.Endpoint.Trim(),
            Bucket: options.Bucket.Trim(),
            AccessKey: options.AccessKey.Trim(),
            SecretKey: options.SecretKey.Trim(),
            ForcePathStyle: options.ForcePathStyle,
            UseSsl: options.UseSsl,
            AutoCreateBucket: options.AutoCreateBucket);
    }

    private static void ValidateSettings(ResolvedS3Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new InvalidOperationException("S3 endpoint is not configured. Check admin settings or S3_ENDPOINT.");
        }

        if (string.IsNullOrWhiteSpace(settings.Bucket))
        {
            throw new InvalidOperationException("S3 bucket is not configured. Check admin settings or S3_BUCKET.");
        }

        if (settings.Bucket.Contains('/') || settings.Bucket.Contains('\\'))
        {
            throw new InvalidOperationException("S3 bucket name must not contain '/' or '\\'.");
        }

        if (string.IsNullOrWhiteSpace(settings.AccessKey) || string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvalidOperationException("S3 credentials are not configured. Check admin settings or S3_ACCESS_KEY/S3_SECRET_KEY.");
        }
    }

    private static string ResolveSha256(MetadataCollection metadata)
    {
        if (TryReadMetadata(metadata, "sha256", out var sha256))
        {
            return sha256.ToLowerInvariant();
        }

        if (TryReadMetadata(metadata, "x-amz-meta-sha256", out var prefixedSha256))
        {
            return prefixedSha256.ToLowerInvariant();
        }

        return string.Empty;
    }

    private static bool TryReadMetadata(MetadataCollection metadata, string key, out string value)
    {
        foreach (var metadataKey in metadata.Keys)
        {
            if (!string.Equals(metadataKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = metadata[metadataKey];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            value = raw.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private sealed record ResolvedS3Settings(
        string Endpoint,
        string Bucket,
        string AccessKey,
        string SecretKey,
        bool ForcePathStyle,
        bool UseSsl,
        bool AutoCreateBucket);
}
