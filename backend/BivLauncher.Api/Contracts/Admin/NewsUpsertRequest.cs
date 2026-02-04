using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class NewsUpsertRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(8192)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Source { get; set; } = "manual";

    public bool Pinned { get; set; }

    public bool Enabled { get; set; } = true;
}
