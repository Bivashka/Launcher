using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace BivLauncher.Api.Services;

public sealed class AdminAuditService(
    AppDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AdminAuditService> logger) : IAdminAuditService
{
    private const int MaxDetailsLength = 8192;
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ILogger<AdminAuditService> _logger = logger;

    public async Task WriteAsync(
        string action,
        string actor,
        string entityType,
        string entityId,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var detailsJson = details is null ? string.Empty : JsonSerializer.Serialize(details);
            if (detailsJson.Length > MaxDetailsLength)
            {
                detailsJson = detailsJson[..MaxDetailsLength];
            }

            var requestId = string.Empty;
            var remoteIp = string.Empty;
            var userAgent = string.Empty;
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is not null)
            {
                requestId = httpContext.Request.Headers.TryGetValue("X-Request-Id", out var requestHeader)
                    ? requestHeader.ToString()
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    requestId = httpContext.TraceIdentifier;
                }

                remoteIp = httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedForHeader)
                    ? forwardedForHeader.ToString().Split(',')[0].Trim()
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(remoteIp))
                {
                    remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
                }

                userAgent = httpContext.Request.Headers.UserAgent.ToString();
            }

            _dbContext.AdminAuditLogs.Add(new AdminAuditLog
            {
                Action = Normalize(action, 128),
                Actor = Normalize(actor, 64),
                EntityType = Normalize(entityType, 64),
                EntityId = Normalize(entityId, 256),
                RequestId = Normalize(requestId, 128),
                RemoteIp = Normalize(remoteIp, 64),
                UserAgent = Normalize(userAgent, 512),
                DetailsJson = detailsJson
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist admin audit log entry '{Action}'.", action);
        }
    }

    private static string Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
