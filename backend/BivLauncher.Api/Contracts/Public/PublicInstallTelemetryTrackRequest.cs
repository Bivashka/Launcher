using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Public;

public sealed class PublicInstallTelemetryTrackRequest
{
    [Required]
    [MaxLength(128)]
    public string ProjectName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string LauncherVersion { get; set; } = string.Empty;
}
