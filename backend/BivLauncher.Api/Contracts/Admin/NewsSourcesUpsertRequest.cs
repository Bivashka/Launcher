using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class NewsSourcesUpsertRequest
{
    [Required]
    public List<NewsSourceUpsertItem> Sources { get; set; } = [];
}

public sealed class NewsSourceUpsertItem
{
    public Guid? Id { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(16)]
    public string Type { get; set; } = "rss";

    [Required]
    [MinLength(1)]
    [MaxLength(1024)]
    public string Url { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    [Range(1, 20)]
    public int MaxItems { get; set; } = 5;

    [Range(1, 1440)]
    public int MinFetchIntervalMinutes { get; set; } = 10;
}
