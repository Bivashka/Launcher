using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class BrandingSettingsUpsertRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(128)]
    public string ProductName { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(128)]
    public string DeveloperName { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(256)]
    public string Tagline { get; set; } = string.Empty;

    [MaxLength(512)]
    public string SupportUrl { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PrimaryColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string AccentColor { get; set; } = string.Empty;

    [MaxLength(32)]
    public string LogoText { get; set; } = string.Empty;
}
