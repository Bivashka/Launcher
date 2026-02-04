namespace BivLauncher.Api.Options;

public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public string FilePath { get; set; } = "branding.json";
}
