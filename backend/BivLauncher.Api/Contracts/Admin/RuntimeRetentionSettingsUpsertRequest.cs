using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class RuntimeRetentionSettingsUpsertRequest
{
    [Required]
    public bool Enabled { get; set; }

    [Range(5, 10080)]
    public int IntervalMinutes { get; set; } = 360;

    [Range(0, 100)]
    public int KeepLast { get; set; } = 3;
}
