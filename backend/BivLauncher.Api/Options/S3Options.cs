namespace BivLauncher.Api.Options;

public sealed class S3Options
{
    public const string SectionName = "S3";

    public string Endpoint { get; set; } = "http://localhost:9000";
    public string Bucket { get; set; } = "launcher-files";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public bool ForcePathStyle { get; set; } = true;
    public bool UseSsl { get; set; } = false;
    public bool AutoCreateBucket { get; set; } = true;
}
