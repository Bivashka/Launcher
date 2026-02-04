namespace BivLauncher.Api.Contracts.Admin;

public sealed record AdminAuditLogDto(
    Guid Id,
    string Action,
    string Actor,
    string EntityType,
    string EntityId,
    string RequestId,
    string RemoteIp,
    string UserAgent,
    string DetailsJson,
    DateTime CreatedAtUtc);
