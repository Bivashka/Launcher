namespace BivLauncher.Client.Models;

public sealed class PublicAuthLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string HwidFingerprint { get; set; } = string.Empty;
    public string HwidHash { get; set; } = string.Empty;
    public string TwoFactorCode { get; set; } = string.Empty;
}

public sealed class PublicAuthLoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public string Username { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public bool RequiresTwoFactor { get; set; }
    public bool TwoFactorEnrolled { get; set; } = true;
    public string TwoFactorProvisioningUri { get; set; } = string.Empty;
    public string TwoFactorSecret { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class PublicAuthSessionResponse
{
    public string Username { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
}
