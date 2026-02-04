namespace BivLauncher.Api.Options;

public sealed class AuthProviderOptions
{
    public const string SectionName = "AuthProvider";

    public string LoginUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
    public bool AllowDevFallback { get; set; } = true;
}
