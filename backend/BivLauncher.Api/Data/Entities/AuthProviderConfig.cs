namespace BivLauncher.Api.Data.Entities;

public sealed class AuthProviderConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AuthMode { get; set; } = "external";
    public string LoginUrl { get; set; } = string.Empty;
    public string LoginFieldKey { get; set; } = "username";
    public string PasswordFieldKey { get; set; } = "password";
    public int TimeoutSeconds { get; set; } = 15;
    public bool AllowDevFallback { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
