namespace BivLauncher.Api.Data.Entities;

public sealed class RuntimeRetentionConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; } = 360;
    public int KeepLast { get; set; } = 3;
    public DateTime? LastRunAtUtc { get; set; }
    public int LastDeletedItems { get; set; }
    public int LastProfilesProcessed { get; set; }
    public string LastRunError { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
