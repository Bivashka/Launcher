using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Public;

public sealed class PublicAuthLoginRequest
{
    [Required]
    [MinLength(2)]
    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(256)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(128)]
    public string HwidFingerprint { get; set; } = string.Empty;

    [MaxLength(128)]
    public string HwidHash { get; set; } = string.Empty;

    [MaxLength(128)]
    public string DeviceUserName { get; set; } = string.Empty;

    [MaxLength(16)]
    public string TwoFactorCode { get; set; } = string.Empty;
}
