namespace BivLauncher.Api.Data.Entities;

public sealed class Build
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public string LoaderType { get; set; } = "vanilla";
    public string McVersion { get; set; } = "1.21.1";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = BuildStatus.Pending;
    public string ManifestKey { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public int FilesCount { get; set; }
    public long TotalSizeBytes { get; set; }

    public Profile? Profile { get; set; }
}

public static class BuildStatus
{
    public const string Pending = "pending";
    public const string Building = "building";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
