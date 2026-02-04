namespace BivLauncher.Api.Data.Entities;

public sealed class DiscordRpcConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ScopeType { get; set; } = string.Empty; // "profile" | "server"
    public Guid ScopeId { get; set; }
    public bool Enabled { get; set; } = true;
    public string AppId { get; set; } = string.Empty;
    public string DetailsText { get; set; } = string.Empty;
    public string StateText { get; set; } = string.Empty;
    public string LargeImageKey { get; set; } = string.Empty;
    public string LargeImageText { get; set; } = string.Empty;
    public string SmallImageKey { get; set; } = string.Empty;
    public string SmallImageText { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
