namespace BivLauncher.Api.Services;

public interface IStorageMigrationService
{
    Task<StorageMigrationResult> MigrateAsync(
        StorageConnectionSettings source,
        StorageConnectionSettings target,
        StorageMigrationOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record StorageConnectionSettings(
    bool UseS3,
    string LocalRootPath,
    string Endpoint,
    string Bucket,
    string AccessKey,
    string SecretKey,
    bool ForcePathStyle,
    bool UseSsl,
    bool AutoCreateBucket);

public sealed record StorageMigrationOptions(
    bool DryRun,
    bool Overwrite,
    int MaxObjects,
    string Prefix);

public sealed record StorageMigrationResult(
    bool DryRun,
    bool SourceUseS3,
    bool TargetUseS3,
    int Scanned,
    int Copied,
    int Skipped,
    int Failed,
    long CopiedBytes,
    bool Truncated,
    long DurationMs,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    IReadOnlyList<string> Errors);
