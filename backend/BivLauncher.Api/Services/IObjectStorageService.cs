namespace BivLauncher.Api.Services;

public interface IObjectStorageService
{
    Task UploadAsync(
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
    Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<StoredObjectMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredObjectListItem>> ListByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record StoredObject(byte[] Data, string ContentType);
public sealed record StoredObjectMetadata(long SizeBytes, string ContentType, string Sha256);
public sealed record StoredObjectListItem(string Key, long SizeBytes, DateTime LastModifiedUtc);
