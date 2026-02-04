using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class NewsRetentionSettingsUpsertRequest
{
    public bool Enabled { get; set; }

    [Range(50, 10000)]
    public int MaxItems { get; set; } = 500;

    [Range(1, 3650)]
    public int MaxAgeDays { get; set; } = 30;
}
