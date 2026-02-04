namespace BivLauncher.Api.Services;

public interface IAdminAuditService
{
    Task WriteAsync(
        string action,
        string actor,
        string entityType,
        string entityId,
        object? details = null,
        CancellationToken cancellationToken = default);
}
