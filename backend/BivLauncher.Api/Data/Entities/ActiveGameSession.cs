namespace BivLauncher.Api.Data.Entities;

public sealed class ActiveGameSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string HwidHash { get; set; } = string.Empty;
    public string DeviceUserName { get; set; } = string.Empty;
    public Guid? ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeatAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(3);

    public AuthAccount? Account { get; set; }
}
