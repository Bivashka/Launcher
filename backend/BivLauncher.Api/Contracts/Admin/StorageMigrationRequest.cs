using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class StorageMigrationRequest
{
    public bool TargetUseS3 { get; set; }
    public bool DryRun { get; set; } = true;
    public bool Overwrite { get; set; } = true;

    [Range(1, 500000)]
    public int MaxObjects { get; set; } = 5000;

    [MaxLength(512)]
    public string Prefix { get; set; } = string.Empty;
}
