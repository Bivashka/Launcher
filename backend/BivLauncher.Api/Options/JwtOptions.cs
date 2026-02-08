namespace BivLauncher.Api.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "BivLauncher.Api";
    public string Audience { get; set; } = "BivLauncher.Admin";
    public int ExpireMinutes { get; set; } = 120;
    public int PlayerExpireDays { get; set; } = 36500;
}
