namespace BivLauncher.Api.Data.Entities;

public sealed class AuthProviderConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LoginUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
    public bool AllowDevFallback { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
