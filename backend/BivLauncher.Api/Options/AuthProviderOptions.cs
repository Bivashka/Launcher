namespace BivLauncher.Api.Options;

public sealed class AuthProviderOptions
{
    public const string SectionName = "AuthProvider";

    public string AuthMode { get; set; } = "external";
    public string LoginUrl { get; set; } = string.Empty;
    public string LoginFieldKey { get; set; } = "username";
    public string PasswordFieldKey { get; set; } = "password";
    public int TimeoutSeconds { get; set; } = 15;
    public bool AllowDevFallback { get; set; } = true;
}
