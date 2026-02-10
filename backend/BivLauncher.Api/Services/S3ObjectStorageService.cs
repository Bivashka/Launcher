using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class S3ObjectStorageService(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<S3Options> fallbackOptions,
    IHostEnvironment hostEnvironment,
    ILogger<S3ObjectStorageService> logger) : IObjectStorageService
{
    private static readonly TimeSpan SettingsCacheDuration = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions LocalMetadataJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly S3Options _fallbackOptions = fallbackOptions.Value;
    private readonly IHostEnvironment _hostEnvironment = hostEnvironment;
    private readonly ILogger<S3ObjectStorageService> _logger = logger;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private readonly SemaphoreSlim _bucketLock = new(1, 1);

    private ResolvedStorageSettings? _cachedSettings;
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
        if (settings.UseS3)
        {
            var client = await GetClientAsync(settings, cancellationToken);
            await EnsureBucketExistsAsync(client, settings, cancellationToken);
            await UploadS3Async(client, settings, key, content, contentType, metadata, cancellationToken);
            return;
        }

        await UploadLocalAsync(settings, key, content, contentType, metadata, cancellationToken);
    }

    public async Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        return settings.UseS3
            ? await GetS3Async(settings, key, cancellationToken)
            : await GetLocalAsync(settings, key, cancellationToken);
    }

    public async Task<StoredObjectMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        return settings.UseS3
            ? await GetS3MetadataAsync(settings, key, cancellationToken)
            : await GetLocalMetadataAsync(settings, key, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredObjectListItem>> ListByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        return settings.UseS3
            ? await ListS3ByPrefixAsync(settings, prefix, cancellationToken)
            : ListLocalByPrefix(settings, prefix);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(cancellationToken);
        if (settings.UseS3)
        {
            var client = await GetClientAsync(settings, cancellationToken);
            await EnsureBucketExistsAsync(client, settings, cancellationToken);

            await client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = settings.Bucket,
                Key = NormalizeStorageKey(key)
            }, cancellationToken);
            return;
        }

        DeleteLocal(settings, key);
    }

    private async Task<ResolvedStorageSettings> ResolveSettingsAsync(CancellationToken cancellationToken)
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

    private async Task<AmazonS3Client> GetClientAsync(ResolvedStorageSettings settings, CancellationToken cancellationToken)
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

    private static string BuildClientSignature(ResolvedStorageSettings settings)
    {
        return string.Join('|',
            settings.UseS3,
            settings.Endpoint,
            settings.AccessKey,
            settings.SecretKey,
            settings.ForcePathStyle,
            settings.UseSsl);
    }

    private async Task EnsureBucketExistsAsync(
        IAmazonS3 client,
        ResolvedStorageSettings settings,
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

    private static async Task UploadS3Async(
        IAmazonS3 client,
        ResolvedStorageSettings settings,
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = settings.Bucket,
            Key = NormalizeStorageKey(key),
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

    private async Task<StoredObject?> GetS3Async(
        ResolvedStorageSettings settings,
        string key,
        CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(settings, cancellationToken);
        await EnsureBucketExistsAsync(client, settings, cancellationToken);

        try
        {
            var response = await client.GetObjectAsync(settings.Bucket, NormalizeStorageKey(key), cancellationToken);
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

    private async Task<StoredObjectMetadata?> GetS3MetadataAsync(
        ResolvedStorageSettings settings,
        string key,
        CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(settings, cancellationToken);
        await EnsureBucketExistsAsync(client, settings, cancellationToken);

        try
        {
            var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = settings.Bucket,
                Key = NormalizeStorageKey(key)
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

    private async Task<IReadOnlyList<StoredObjectListItem>> ListS3ByPrefixAsync(
        ResolvedStorageSettings settings,
        string prefix,
        CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(settings, cancellationToken);
        await EnsureBucketExistsAsync(client, settings, cancellationToken);

        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : NormalizeStorageKey(prefix);

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

    private async Task UploadLocalAsync(
        ResolvedStorageSettings settings,
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        var objectPath = ResolveLocalObjectPath(settings, key, createParentDirectory: true);

        await using (var stream = new FileStream(
            objectPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await content.CopyToAsync(stream, cancellationToken);
        }

        var document = new LocalObjectMetadataDocument
        {
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            Metadata = metadata?
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        await WriteLocalMetadataAsync(objectPath, document, cancellationToken);
    }

    private async Task<StoredObject?> GetLocalAsync(
        ResolvedStorageSettings settings,
        string key,
        CancellationToken cancellationToken)
    {
        var objectPath = ResolveLocalObjectPath(settings, key, createParentDirectory: false);
        if (!File.Exists(objectPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(objectPath, cancellationToken);
        var metadata = await TryReadLocalMetadataAsync(objectPath, cancellationToken);
        var contentType = string.IsNullOrWhiteSpace(metadata?.ContentType)
            ? InferContentTypeFromKey(key)
            : metadata.ContentType!.Trim();

        return new StoredObject(bytes, contentType);
    }

    private async Task<StoredObjectMetadata?> GetLocalMetadataAsync(
        ResolvedStorageSettings settings,
        string key,
        CancellationToken cancellationToken)
    {
        var objectPath = ResolveLocalObjectPath(settings, key, createParentDirectory: false);
        if (!File.Exists(objectPath))
        {
            return null;
        }

        var fileInfo = new FileInfo(objectPath);
        var metadata = await TryReadLocalMetadataAsync(objectPath, cancellationToken);
        var contentType = string.IsNullOrWhiteSpace(metadata?.ContentType)
            ? InferContentTypeFromKey(key)
            : metadata.ContentType!.Trim();

        var sha256 = string.Empty;
        if (metadata?.Metadata is not null &&
            metadata.Metadata.TryGetValue("sha256", out var storedSha256) &&
            !string.IsNullOrWhiteSpace(storedSha256))
        {
            sha256 = storedSha256.Trim().ToLowerInvariant();
        }
        else
        {
            await using var stream = new FileStream(
                objectPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        }

        return new StoredObjectMetadata(fileInfo.Length, contentType, sha256);
    }

    private IReadOnlyList<StoredObjectListItem> ListLocalByPrefix(ResolvedStorageSettings settings, string prefix)
    {
        var rootPath = ResolveLocalRootPath(settings, ensureExists: false);
        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : NormalizeStorageKey(prefix);

        var files = Directory
            .EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
            .Select(path =>
            {
                var relative = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
                var info = new FileInfo(path);
                return new StoredObjectListItem(relative, info.Length, info.LastWriteTimeUtc);
            })
            .Where(item => string.IsNullOrWhiteSpace(normalizedPrefix) ||
                           item.Key.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return files;
    }

    private void DeleteLocal(ResolvedStorageSettings settings, string key)
    {
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

    private string ResolveLocalObjectPath(ResolvedStorageSettings settings, string key, bool createParentDirectory)
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

    private string ResolveLocalRootPath(ResolvedStorageSettings settings, bool ensureExists)
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

    private static ResolvedStorageSettings ResolveFromStorage(S3StorageConfig config)
    {
        return new ResolvedStorageSettings(
            UseS3: config.UseS3,
            LocalRootPath: string.IsNullOrWhiteSpace(config.LocalRootPath) ? "Storage" : config.LocalRootPath.Trim(),
            Endpoint: config.Endpoint.Trim(),
            Bucket: config.Bucket.Trim(),
            AccessKey: config.AccessKey.Trim(),
            SecretKey: config.SecretKey.Trim(),
            ForcePathStyle: config.ForcePathStyle,
            UseSsl: config.UseSsl,
            AutoCreateBucket: config.AutoCreateBucket);
    }

    private static ResolvedStorageSettings ResolveFromFallback(S3Options options)
    {
        return new ResolvedStorageSettings(
            UseS3: options.UseS3,
            LocalRootPath: string.IsNullOrWhiteSpace(options.LocalRootPath) ? "Storage" : options.LocalRootPath.Trim(),
            Endpoint: options.Endpoint.Trim(),
            Bucket: options.Bucket.Trim(),
            AccessKey: options.AccessKey.Trim(),
            SecretKey: options.SecretKey.Trim(),
            ForcePathStyle: options.ForcePathStyle,
            UseSsl: options.UseSsl,
            AutoCreateBucket: options.AutoCreateBucket);
    }

    private static void ValidateSettings(ResolvedStorageSettings settings)
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
            throw new InvalidOperationException("S3 endpoint is not configured. Check admin settings or S3_ENDPOINT.");
        }

        if (Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpointUri))
        {
            var host = endpointUri.Host.Trim().ToLowerInvariant();
            var runningInContainer = string.Equals(
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                "true",
                StringComparison.OrdinalIgnoreCase);

            if (runningInContainer && (host == "localhost" || host == "127.0.0.1"))
            {
                throw new InvalidOperationException(
                    "S3 endpoint points to localhost inside container. Use service hostname (for docker-compose usually http://minio:9000) or disable S3.");
            }
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
            ".jar2" => "application/java-archive",
            _ => "application/octet-stream"
        };
    }

    private sealed record ResolvedStorageSettings(
        bool UseS3,
        string LocalRootPath,
        string Endpoint,
        string Bucket,
        string AccessKey,
        string SecretKey,
        bool ForcePathStyle,
        bool UseSsl,
        bool AutoCreateBucket);

    private sealed class LocalObjectMetadataDocument
    {
        public string ContentType { get; set; } = "application/octet-stream";
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
