using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class AdminChangePasswordRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(256)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(256)]
    public string NewPassword { get; set; } = string.Empty;
}
