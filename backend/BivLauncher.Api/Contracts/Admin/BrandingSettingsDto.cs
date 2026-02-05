namespace BivLauncher.Api.Contracts.Admin;

public sealed record BrandingSettingsDto(
    string ProductName,
    string DeveloperName,
    string Tagline,
    string SupportUrl,
    string PrimaryColor,
    string AccentColor,
    string LogoText,
    string BackgroundImageUrl,
    double BackgroundOverlayOpacity,
    string LoginCardPosition,
    int LoginCardWidth);
