using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class ProfileUpsertRequest
{
    [Required]
    [MinLength(2)]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MinLength(2)]
    [MaxLength(64)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string Description { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    [MaxLength(512)]
    public string IconKey { get; set; } = string.Empty;

    [Range(0, 10000)]
    public int Priority { get; set; } = 100;

    [Range(512, 65536)]
    public int RecommendedRamMb { get; set; } = 2048;

    [MaxLength(512)]
    public string BundledJavaPath { get; set; } = string.Empty;

    [MaxLength(512)]
    public string BundledRuntimeKey { get; set; } = string.Empty;
}
