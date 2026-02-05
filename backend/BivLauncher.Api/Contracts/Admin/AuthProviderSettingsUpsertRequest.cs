using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class AuthProviderSettingsUpsertRequest
{
    [MaxLength(16)]
    public string AuthMode { get; set; } = "external";

    [MaxLength(512)]
    public string LoginUrl { get; set; } = string.Empty;

    [MaxLength(64)]
    public string LoginFieldKey { get; set; } = "username";

    [MaxLength(64)]
    public string PasswordFieldKey { get; set; } = "password";

    [Range(5, 120)]
    public int TimeoutSeconds { get; set; } = 15;

    public bool AllowDevFallback { get; set; } = true;
}
