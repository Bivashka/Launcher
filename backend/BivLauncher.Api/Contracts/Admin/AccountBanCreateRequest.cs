using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class AccountBanCreateRequest
{
    [MaxLength(1024)]
    public string Reason { get; set; } = string.Empty;

    public DateTime? ExpiresAtUtc { get; set; }
}
