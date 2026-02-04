using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class NewsSyncSettingsUpsertRequest
{
    public bool Enabled { get; set; }

    [Range(5, 1440)]
    public int IntervalMinutes { get; set; } = 60;
}
