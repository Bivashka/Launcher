using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class DocumentationArticleUpsertRequest
{
    [Required]
    [MaxLength(96)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Category { get; set; } = "docs";

    [MaxLength(1024)]
    public string Summary { get; set; } = string.Empty;

    [Required]
    [MaxLength(64000)]
    public string BodyMarkdown { get; set; } = string.Empty;

    [Range(0, 10000)]
    public int Order { get; set; } = 100;

    public bool Published { get; set; } = true;
}
