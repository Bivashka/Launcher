using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/crashes")]
public sealed class AdminCrashesController(
    AppDbContext dbContext,
    IAdminAuditService auditService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CrashReportDto>>> List(
        [FromQuery] string? status = null,
        [FromQuery] string? profileSlug = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeFilters(status, profileSlug, search, fromUtc, toUtc, out var filters, out var error))
        {
            return BadRequest(new { error });
        }

        var normalizedLimit = Math.Clamp(limit, 1, 500);
        var normalizedOffset = Math.Max(0, offset);

        var query = ApplyFilters(dbContext.CrashReports.AsNoTracking(), filters)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id);

        var items = await query
            .Skip(normalizedOffset)
            .Take(normalizedLimit)
            .Select(MapDtoExpression())
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<CrashReportDto>> UpdateStatus(
        Guid id,
        [FromBody] CrashReportStatusUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeStatus(request.Status);
        if (string.IsNullOrWhiteSpace(normalizedStatus))
        {
            return BadRequest(new { error = "status must be either 'new' or 'resolved'." });
        }

        var entity = await dbContext.CrashReports.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var previousStatus = entity.Status;
        if (!string.Equals(previousStatus, normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            entity.Status = normalizedStatus;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.ResolvedAtUtc = normalizedStatus == "resolved" ? DateTime.UtcNow : null;
            await dbContext.SaveChangesAsync(cancellationToken);

            var actor = User.Identity?.Name ?? "admin";
            await auditService.WriteAsync(
                action: "crash.status.update",
                actor: actor,
                entityType: "crash",
                entityId: entity.Id.ToString(),
                details: new
                {
                    entity.CrashId,
                    from = previousStatus,
                    to = normalizedStatus
                },
                cancellationToken: cancellationToken);
        }

        return Ok(Map(entity));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? format = "json",
        [FromQuery] string? status = null,
        [FromQuery] string? profileSlug = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int limit = 5000,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeFilters(status, profileSlug, search, fromUtc, toUtc, out var filters, out var error))
        {
            return BadRequest(new { error });
        }

        var normalizedFormat = (format ?? "json").Trim().ToLowerInvariant();
        if (normalizedFormat is not ("json" or "csv"))
        {
            return BadRequest(new { error = "format must be either 'json' or 'csv'." });
        }

        var normalizedLimit = Math.Clamp(limit, 1, 50000);
        var items = await ApplyFilters(dbContext.CrashReports.AsNoTracking(), filters)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(normalizedLimit)
            .Select(MapDtoExpression())
            .ToListAsync(cancellationToken);

        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "crash.export",
            actor: actor,
            entityType: "crash",
            entityId: "export",
            details: new
            {
                normalizedFormat,
                normalizedLimit,
                filters.Status,
                filters.ProfileSlug,
                filters.Search,
                filters.FromUtc,
                filters.ToUtc,
                exported = items.Count
            },
            cancellationToken: cancellationToken);

        var fileSuffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        if (normalizedFormat == "csv")
        {
            var csv = BuildCsv(items);
            return File(
                Encoding.UTF8.GetBytes(csv),
                "text/csv; charset=utf-8",
                $"crashes-{fileSuffix}.csv");
        }

        var json = JsonSerializer.Serialize(items);
        return File(
            Encoding.UTF8.GetBytes(json),
            "application/json; charset=utf-8",
            $"crashes-{fileSuffix}.json");
    }

    private static IQueryable<CrashReport> ApplyFilters(IQueryable<CrashReport> query, CrashFilters filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.Status))
        {
            query = query.Where(x => x.Status == filters.Status);
        }

        if (!string.IsNullOrWhiteSpace(filters.ProfileSlug))
        {
            query = query.Where(x => x.ProfileSlug == filters.ProfileSlug);
        }

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            query = query.Where(x =>
                x.CrashId.Contains(filters.Search) ||
                x.Reason.Contains(filters.Search) ||
                x.ErrorType.Contains(filters.Search) ||
                x.LogExcerpt.Contains(filters.Search));
        }

        if (filters.FromUtc.HasValue)
        {
            query = query.Where(x => x.CreatedAtUtc >= filters.FromUtc.Value);
        }

        if (filters.ToUtc.HasValue)
        {
            query = query.Where(x => x.CreatedAtUtc <= filters.ToUtc.Value);
        }

        return query;
    }

    private static bool TryNormalizeFilters(
        string? status,
        string? profileSlug,
        string? search,
        DateTime? fromUtc,
        DateTime? toUtc,
        out CrashFilters filters,
        out string error)
    {
        var normalizedStatus = NormalizeStatus(status);
        if (!string.IsNullOrWhiteSpace(status) && string.IsNullOrWhiteSpace(normalizedStatus))
        {
            filters = default;
            error = "status must be either 'new' or 'resolved'.";
            return false;
        }

        var normalizedFromUtc = fromUtc?.ToUniversalTime();
        var normalizedToUtc = toUtc?.ToUniversalTime();
        if (normalizedFromUtc.HasValue && normalizedToUtc.HasValue && normalizedFromUtc > normalizedToUtc)
        {
            filters = default;
            error = "fromUtc must be less than or equal to toUtc.";
            return false;
        }

        filters = new CrashFilters(
            normalizedStatus,
            (profileSlug ?? string.Empty).Trim().ToLowerInvariant(),
            (search ?? string.Empty).Trim(),
            normalizedFromUtc,
            normalizedToUtc);
        error = string.Empty;
        return true;
    }

    private static string NormalizeStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized is "new" or "resolved"
            ? normalized
            : string.Empty;
    }

    private static Expression<Func<CrashReport, CrashReportDto>> MapDtoExpression()
    {
        return x => new CrashReportDto(
            x.Id,
            x.CrashId,
            x.Status,
            x.ProfileSlug,
            x.ServerName,
            x.RouteCode,
            x.LauncherVersion,
            x.OsVersion,
            x.JavaVersion,
            x.ExitCode,
            x.Reason,
            x.ErrorType,
            x.LogExcerpt,
            x.MetadataJson,
            x.OccurredAtUtc,
            x.CreatedAtUtc,
            x.UpdatedAtUtc,
            x.ResolvedAtUtc);
    }

    private static CrashReportDto Map(CrashReport entity)
    {
        return new CrashReportDto(
            entity.Id,
            entity.CrashId,
            entity.Status,
            entity.ProfileSlug,
            entity.ServerName,
            entity.RouteCode,
            entity.LauncherVersion,
            entity.OsVersion,
            entity.JavaVersion,
            entity.ExitCode,
            entity.Reason,
            entity.ErrorType,
            entity.LogExcerpt,
            entity.MetadataJson,
            entity.OccurredAtUtc,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.ResolvedAtUtc);
    }

    private static string BuildCsv(IReadOnlyList<CrashReportDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("id,crashId,status,profileSlug,serverName,routeCode,launcherVersion,osVersion,javaVersion,exitCode,reason,errorType,occurredAtUtc,createdAtUtc,updatedAtUtc,resolvedAtUtc");
        foreach (var item in items)
        {
            sb.Append(EscapeCsv(item.Id.ToString())).Append(',')
                .Append(EscapeCsv(item.CrashId)).Append(',')
                .Append(EscapeCsv(item.Status)).Append(',')
                .Append(EscapeCsv(item.ProfileSlug)).Append(',')
                .Append(EscapeCsv(item.ServerName)).Append(',')
                .Append(EscapeCsv(item.RouteCode)).Append(',')
                .Append(EscapeCsv(item.LauncherVersion)).Append(',')
                .Append(EscapeCsv(item.OsVersion)).Append(',')
                .Append(EscapeCsv(item.JavaVersion)).Append(',')
                .Append(EscapeCsv(item.ExitCode?.ToString() ?? string.Empty)).Append(',')
                .Append(EscapeCsv(item.Reason)).Append(',')
                .Append(EscapeCsv(item.ErrorType)).Append(',')
                .Append(EscapeCsv(item.OccurredAtUtc.ToString("O"))).Append(',')
                .Append(EscapeCsv(item.CreatedAtUtc.ToString("O"))).Append(',')
                .Append(EscapeCsv(item.UpdatedAtUtc.ToString("O"))).Append(',')
                .Append(EscapeCsv(item.ResolvedAtUtc?.ToString("O") ?? string.Empty))
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

    private readonly record struct CrashFilters(
        string Status,
        string ProfileSlug,
        string Search,
        DateTime? FromUtc,
        DateTime? ToUtc);
}
