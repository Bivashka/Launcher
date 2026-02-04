namespace BivLauncher.Api.Data.Entities;

public sealed class AdminAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string RemoteIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
