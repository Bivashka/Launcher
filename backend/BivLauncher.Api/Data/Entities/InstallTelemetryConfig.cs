namespace BivLauncher.Api.Data.Entities;

public sealed class InstallTelemetryConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
