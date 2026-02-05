namespace BivLauncher.Api.Data.Entities;

public sealed class TwoFactorConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
