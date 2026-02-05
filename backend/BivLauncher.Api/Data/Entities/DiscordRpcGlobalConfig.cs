namespace BivLauncher.Api.Data.Entities;

public sealed class DiscordRpcGlobalConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public bool PrivacyMode { get; set; } = false;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
