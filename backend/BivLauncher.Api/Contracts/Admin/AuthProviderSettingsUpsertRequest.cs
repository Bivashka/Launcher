using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class AuthProviderSettingsUpsertRequest
{
    [MaxLength(512)]
    public string LoginUrl { get; set; } = string.Empty;

    [Range(5, 120)]
    public int TimeoutSeconds { get; set; } = 15;

    public bool AllowDevFallback { get; set; } = true;
}
