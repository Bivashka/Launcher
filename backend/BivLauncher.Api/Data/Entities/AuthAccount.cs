namespace BivLauncher.Api.Data.Entities;

public sealed class AuthAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Roles { get; set; } = "player";
    public bool Banned { get; set; }
    public int SessionVersion { get; set; }
    public string HwidHash { get; set; } = string.Empty;
    public string DeviceUserName { get; set; } = string.Empty;
    public bool TwoFactorRequired { get; set; }
    public string TwoFactorSecret { get; set; } = string.Empty;
    public DateTime? TwoFactorEnrolledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
