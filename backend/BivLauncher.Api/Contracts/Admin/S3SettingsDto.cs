namespace BivLauncher.Api.Contracts.Admin;

public sealed record S3SettingsDto(
    bool UseS3,
    string LocalRootPath,
    string Endpoint,
    string Bucket,
    string AccessKey,
    string SecretKey,
    bool ForcePathStyle,
    bool UseSsl,
    bool AutoCreateBucket,
    DateTime? UpdatedAtUtc);
