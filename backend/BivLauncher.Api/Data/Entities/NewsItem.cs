namespace BivLauncher.Api.Data.Entities;

public sealed class NewsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string Source { get; set; } = "manual";
    public bool Pinned { get; set; } = false;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
