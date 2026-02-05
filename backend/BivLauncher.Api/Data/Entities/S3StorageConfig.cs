namespace BivLauncher.Api.Data.Entities;

public sealed class S3StorageConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool UseS3 { get; set; } = true;
    public string LocalRootPath { get; set; } = "Storage";
    public string Endpoint { get; set; } = string.Empty;
    public string Bucket { get; set; } = "launcher-files";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; } = true;
    public bool UseSsl { get; set; }
    public bool AutoCreateBucket { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
