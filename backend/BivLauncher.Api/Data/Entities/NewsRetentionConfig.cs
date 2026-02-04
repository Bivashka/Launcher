namespace BivLauncher.Api.Data.Entities;

public sealed class NewsRetentionConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; }
    public int MaxItems { get; set; } = 500;
    public int MaxAgeDays { get; set; } = 30;
    public DateTime? LastAppliedAtUtc { get; set; }
    public int LastDeletedItems { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
