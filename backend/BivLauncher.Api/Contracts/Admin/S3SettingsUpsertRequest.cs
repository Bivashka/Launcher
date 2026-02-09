using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class S3SettingsUpsertRequest
{
    public bool UseS3 { get; set; } = false;

    [MaxLength(1024)]
    public string LocalRootPath { get; set; } = "Storage";

    [MaxLength(512)]
    public string Endpoint { get; set; } = "http://minio:9000";

    [MaxLength(128)]
    public string Bucket { get; set; } = string.Empty;

    [MaxLength(256)]
    public string AccessKey { get; set; } = string.Empty;

    [MaxLength(256)]
    public string SecretKey { get; set; } = string.Empty;

    public bool ForcePathStyle { get; set; } = true;
    public bool UseSsl { get; set; }
    public bool AutoCreateBucket { get; set; } = true;
}
