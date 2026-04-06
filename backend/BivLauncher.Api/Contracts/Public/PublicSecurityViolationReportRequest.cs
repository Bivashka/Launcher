using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Public;

public sealed class PublicSecurityViolationReportRequest
{
    [MaxLength(256)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string Evidence { get; set; } = string.Empty;

    [MaxLength(128)]
    public string HwidFingerprint { get; set; } = string.Empty;

    [MaxLength(128)]
    public string DeviceUserName { get; set; } = string.Empty;
}
