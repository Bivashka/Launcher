namespace BivLauncher.Api.Data.Entities;

public sealed class NewsSyncConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 60;
    public DateTime? LastRunAtUtc { get; set; }
    public string LastRunError { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
