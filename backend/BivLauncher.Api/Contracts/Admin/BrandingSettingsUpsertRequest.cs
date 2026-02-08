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

    [MaxLength(64)]
    public string SurfaceColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string SurfaceBorderColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TextPrimaryColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string TextSecondaryColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PrimaryButtonColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PrimaryButtonBorderColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PrimaryButtonTextColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PlayButtonColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PlayButtonBorderColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PlayButtonTextColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string InputBackgroundColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string InputBorderColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string InputTextColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ListBackgroundColor { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ListBorderColor { get; set; } = string.Empty;

    [MaxLength(32)]
    public string LogoText { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string BackgroundImageUrl { get; set; } = string.Empty;

    [Range(0, 0.95)]
    public double BackgroundOverlayOpacity { get; set; } = 0.55;

    [MaxLength(16)]
    public string LoginCardPosition { get; set; } = "center";

    [Range(340, 640)]
    public int LoginCardWidth { get; set; } = 460;
}
