namespace BivLauncher.Api.Data.Entities;

public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string IconKey { get; set; } = string.Empty;
    public int Priority { get; set; } = 100;
    public int RecommendedRamMb { get; set; } = 2048;
    public string JvmArgsDefault { get; set; } = string.Empty;
    public string GameArgsDefault { get; set; } = string.Empty;
    public string BundledJavaPath { get; set; } = string.Empty;
    public string BundledRuntimeKey { get; set; } = string.Empty;
    public string BundledRuntimeSha256 { get; set; } = string.Empty;
    public long BundledRuntimeSizeBytes { get; set; }
    public string BundledRuntimeContentType { get; set; } = string.Empty;
    public string LatestBuildId { get; set; } = string.Empty;
    public string LatestManifestKey { get; set; } = string.Empty;
    public string LatestClientVersion { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Server> Servers { get; set; } = new List<Server>();
    public ICollection<Build> Builds { get; set; } = new List<Build>();
}
