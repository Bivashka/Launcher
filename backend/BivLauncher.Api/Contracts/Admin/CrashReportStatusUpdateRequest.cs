using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class CrashReportStatusUpdateRequest
{
    [Required]
    [MaxLength(16)]
    public string Status { get; set; } = "new";
}
