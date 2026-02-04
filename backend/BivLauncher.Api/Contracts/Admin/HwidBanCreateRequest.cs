using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class HwidBanCreateRequest
{
    [Required]
    [MinLength(16)]
    [MaxLength(128)]
    public string HwidHash { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Reason { get; set; } = string.Empty;

    public DateTime? ExpiresAtUtc { get; set; }
}
