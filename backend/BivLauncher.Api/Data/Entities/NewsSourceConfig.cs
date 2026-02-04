namespace BivLauncher.Api.Data.Entities;

public sealed class NewsSourceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "rss";
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int MaxItems { get; set; } = 5;
    public DateTime? LastSyncAtUtc { get; set; }
    public string LastSyncError { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
