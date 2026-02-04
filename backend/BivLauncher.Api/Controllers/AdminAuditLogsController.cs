using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/audit-logs")]
public sealed class AdminAuditLogsController(
    AppDbContext dbContext,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminAuditLogDto>>> List(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? actionPrefix = null,
        [FromQuery] string? actor = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null,
        [FromQuery] string? requestId = null,
        [FromQuery] string? remoteIp = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string? sort = "desc",
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeFilter(
            actionPrefix,
            actor,
            entityType,
            entityId,
            requestId,
            remoteIp,
            fromUtc,
            toUtc,
            sort,
            out var filter,
            out var filterError))
        {
            return BadRequest(new { error = filterError });
        }

        var normalizedLimit = Math.Clamp(limit, 1, 500);
        var normalizedOffset = Math.Max(0, offset);

        var query = ApplySort(
            ApplyFilter(dbContext.AdminAuditLogs.AsNoTracking(), filter),
            filter.SortAscending);

        var items = await query
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(MapDtoExpression())
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] int limit = 5000,
        [FromQuery] string? format = "json",
        [FromQuery] string? actionPrefix = null,
        [FromQuery] string? actor = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null,
        [FromQuery] string? requestId = null,
        [FromQuery] string? remoteIp = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string? sort = "desc",
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeFilter(
            actionPrefix,
            actor,
            entityType,
            entityId,
            requestId,
            remoteIp,
            fromUtc,
            toUtc,
            sort,
            out var filter,
            out var filterError))
        {
            return BadRequest(new { error = filterError });
        }

        var normalizedLimit = Math.Clamp(limit, 1, 50000);
        var normalizedFormat = (format ?? "json").Trim().ToLowerInvariant();
        if (normalizedFormat is not ("json" or "csv"))
        {
            return BadRequest(new { error = "format must be either 'json' or 'csv'." });
        }

        var items = await ApplySort(
                ApplyFilter(dbContext.AdminAuditLogs.AsNoTracking(), filter),
                filter.SortAscending)
            .Take(normalizedLimit)
            .Select(MapDtoExpression())
            .ToListAsync(cancellationToken);

        var actorName = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "audit.export",
            actor: actorName,
            entityType: "audit-logs",
            entityId: "export",
            details: new
            {
                format = normalizedFormat,
                requestedLimit = normalizedLimit,
                exported = items.Count,
                filter.ActionPrefix,
                filter.Actor,
                filter.EntityType,
                filter.EntityId,
                filter.RequestId,
                filter.RemoteIp,
                filter.FromUtc,
                filter.ToUtc,
                sort = filter.SortAscending ? "asc" : "desc"
            },
            cancellationToken: cancellationToken);

        var fileSuffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        if (normalizedFormat == "csv")
        {
            var csv = BuildCsv(items);
            return File(
                Encoding.UTF8.GetBytes(csv),
                "text/csv; charset=utf-8",
                $"admin-audit-{fileSuffix}.csv");
        }

        var json = JsonSerializer.Serialize(items);
        return File(
            Encoding.UTF8.GetBytes(json),
            "application/json; charset=utf-8",
            $"admin-audit-{fileSuffix}.json");
    }

    [HttpDelete]
    public async Task<IActionResult> Cleanup(
        [FromQuery] DateTime? olderThanUtc = null,
        [FromQuery] int limit = 5000,
        [FromQuery] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        if (!olderThanUtc.HasValue)
        {
            return BadRequest(new { error = "olderThanUtc is required." });
        }

        var normalizedOlderThanUtc = olderThanUtc.Value.ToUniversalTime();
        if (normalizedOlderThanUtc > DateTime.UtcNow.AddMinutes(1))
        {
            return BadRequest(new { error = "olderThanUtc must be in the past." });
        }

        var normalizedLimit = Math.Clamp(limit, 1, 50000);

        var eligibleQuery = dbContext.AdminAuditLogs
            .AsNoTracking()
            .Where(x => x.CreatedAtUtc < normalizedOlderThanUtc);

        var totalEligible = await eligibleQuery.CountAsync(cancellationToken);
        var candidates = await eligibleQuery
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Take(normalizedLimit)
            .Select(x => new { x.Id, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var deleted = 0;
        if (!dryRun && candidates.Count > 0)
        {
            var ids = candidates.Select(x => x.Id).ToList();
            var entities = await dbContext.AdminAuditLogs
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken);
            dbContext.AdminAuditLogs.RemoveRange(entities);
            deleted = await dbContext.SaveChangesAsync(cancellationToken);
        }

        var actorName = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "audit.cleanup",
            actor: actorName,
            entityType: "audit-logs",
            entityId: "cleanup",
            details: new
            {
                dryRun,
                olderThanUtc = normalizedOlderThanUtc,
                requestedLimit = normalizedLimit,
                totalEligible,
                candidates = candidates.Count,
                deleted,
                hasMore = totalEligible > candidates.Count
            },
            cancellationToken: cancellationToken);

        return Ok(new
        {
            dryRun,
            olderThanUtc = normalizedOlderThanUtc,
            requestedLimit = normalizedLimit,
            totalEligible,
            candidates = candidates.Count,
            deleted,
            hasMore = totalEligible > candidates.Count,
            oldestCandidateAtUtc = candidates.FirstOrDefault()?.CreatedAtUtc,
            newestCandidateAtUtc = candidates.LastOrDefault()?.CreatedAtUtc
        });
    }

    private static IQueryable<AdminAuditLog> ApplySort(IQueryable<AdminAuditLog> query, bool sortAscending)
    {
        return sortAscending
            ? query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            : query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id);
    }

    private static IQueryable<AdminAuditLog> ApplyFilter(IQueryable<AdminAuditLog> query, AuditLogFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.ActionPrefix))
        {
            query = query.Where(x => x.Action.StartsWith(filter.ActionPrefix));
        }
        if (!string.IsNullOrWhiteSpace(filter.Actor))
        {
            query = query.Where(x => x.Actor == filter.Actor);
        }
        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(x => x.EntityType == filter.EntityType);
        }
        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(x => x.EntityId.Contains(filter.EntityId));
        }
        if (!string.IsNullOrWhiteSpace(filter.RequestId))
        {
            query = query.Where(x => x.RequestId == filter.RequestId);
        }
        if (!string.IsNullOrWhiteSpace(filter.RemoteIp))
        {
            query = query.Where(x => x.RemoteIp == filter.RemoteIp);
        }
        if (filter.FromUtc.HasValue)
        {
            query = query.Where(x => x.CreatedAtUtc >= filter.FromUtc.Value);
        }
        if (filter.ToUtc.HasValue)
        {
            query = query.Where(x => x.CreatedAtUtc <= filter.ToUtc.Value);
        }

        return query;
    }

    private static System.Linq.Expressions.Expression<Func<AdminAuditLog, AdminAuditLogDto>> MapDtoExpression()
    {
        return x => new AdminAuditLogDto(
            x.Id,
            x.Action,
            x.Actor,
            x.EntityType,
            x.EntityId,
            x.RequestId,
            x.RemoteIp,
            x.UserAgent,
            x.DetailsJson,
            x.CreatedAtUtc);
    }

    private static bool TryNormalizeFilter(
        string? actionPrefix,
        string? actor,
        string? entityType,
        string? entityId,
        string? requestId,
        string? remoteIp,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? sort,
        out AuditLogFilter filter,
        out string error)
    {
        var normalizedFromUtc = fromUtc?.ToUniversalTime();
        var normalizedToUtc = toUtc?.ToUniversalTime();
        if (normalizedFromUtc.HasValue && normalizedToUtc.HasValue && normalizedFromUtc > normalizedToUtc)
        {
            filter = new AuditLogFilter(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, null, false);
            error = "fromUtc must be less than or equal to toUtc.";
            return false;
        }

        filter = new AuditLogFilter(
            (actionPrefix ?? string.Empty).Trim(),
            (actor ?? string.Empty).Trim(),
            (entityType ?? string.Empty).Trim(),
            (entityId ?? string.Empty).Trim(),
            (requestId ?? string.Empty).Trim(),
            (remoteIp ?? string.Empty).Trim(),
            normalizedFromUtc,
            normalizedToUtc,
            string.Equals((sort ?? "desc").Trim(), "asc", StringComparison.OrdinalIgnoreCase));
        error = string.Empty;
        return true;
    }

    private static string BuildCsv(IReadOnlyList<AdminAuditLogDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("id,action,actor,entityType,entityId,requestId,remoteIp,userAgent,createdAtUtc,detailsJson");
        foreach (var item in items)
        {
            sb.Append(EscapeCsv(item.Id.ToString())).Append(',')
                .Append(EscapeCsv(item.Action)).Append(',')
                .Append(EscapeCsv(item.Actor)).Append(',')
                .Append(EscapeCsv(item.EntityType)).Append(',')
                .Append(EscapeCsv(item.EntityId)).Append(',')
                .Append(EscapeCsv(item.RequestId)).Append(',')
                .Append(EscapeCsv(item.RemoteIp)).Append(',')
                .Append(EscapeCsv(item.UserAgent)).Append(',')
                .Append(EscapeCsv(item.CreatedAtUtc.ToString("O"))).Append(',')
                .Append(EscapeCsv(item.DetailsJson))
                .AppendLine();
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"");
        if (escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r'))
        {
            return $"\"{escaped}\"";
        }

        return escaped;
    }

    private sealed record AuditLogFilter(
        string ActionPrefix,
        string Actor,
        string EntityType,
        string EntityId,
        string RequestId,
        string RemoteIp,
        DateTime? FromUtc,
        DateTime? ToUtc,
        bool SortAscending);
}
