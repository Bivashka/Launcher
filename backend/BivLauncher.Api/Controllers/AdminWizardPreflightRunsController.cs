using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/wizard/preflight-runs")]
public sealed class AdminWizardPreflightRunsController(
    AppDbContext dbContext,
    IAdminAuditService auditService) : ControllerBase
{
    private const int MaxChecksPerRun = 20;
    private const int MaxStoredRuns = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedStatuses = ["passed", "failed", "skipped"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WizardPreflightRunDto>>> Get(
        [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 50);
        var rows = await dbContext.WizardPreflightRuns
            .AsNoTracking()
            .OrderByDescending(x => x.RanAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<WizardPreflightRunDto>> Create(
        [FromBody] WizardPreflightRunCreateRequest request,
        CancellationToken cancellationToken)
    {
        var checks = NormalizeChecks(request.Checks);
        if (checks.Count == 0)
        {
            return BadRequest(new { error = "At least one pre-flight check is required." });
        }

        if (checks.Count > MaxChecksPerRun)
        {
            return BadRequest(new { error = $"No more than {MaxChecksPerRun} checks are allowed per run." });
        }

        var passedCount = checks.Count(x => string.Equals(x.Status, "passed", StringComparison.OrdinalIgnoreCase));
        var actor = (User.Identity?.Name ?? "admin").Trim();
        if (string.IsNullOrWhiteSpace(actor))
        {
            actor = "admin";
        }

        var run = new WizardPreflightRun
        {
            Actor = actor.Length <= 64 ? actor : actor[..64],
            PassedCount = passedCount,
            TotalCount = checks.Count,
            ChecksJson = JsonSerializer.Serialize(checks, JsonOptions),
            RanAtUtc = DateTime.UtcNow
        };

        dbContext.WizardPreflightRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        var trimmed = await TrimHistoryAsync(cancellationToken);

        await auditService.WriteAsync(
            action: "wizard.preflight.run",
            actor: run.Actor,
            entityType: "wizard",
            entityId: run.Id.ToString(),
            details: new
            {
                run.PassedCount,
                run.TotalCount,
                trimmed
            },
            cancellationToken: cancellationToken);

        return CreatedAtAction(
            nameof(Get),
            new { limit = 1 },
            new WizardPreflightRunDto(
                run.Id,
                run.Actor,
                run.PassedCount,
                run.TotalCount,
                run.RanAtUtc,
                checks));
    }

    [HttpDelete]
    public async Task<ActionResult> Clear(CancellationToken cancellationToken)
    {
        var deleted = await dbContext.WizardPreflightRuns.ExecuteDeleteAsync(cancellationToken);
        var actor = User.Identity?.Name ?? "admin";
        await auditService.WriteAsync(
            action: "wizard.preflight.clear",
            actor: actor,
            entityType: "wizard",
            entityId: "preflight-runs",
            details: new { deleted },
            cancellationToken: cancellationToken);

        return Ok(new { deleted });
    }

    private static List<WizardPreflightCheckDto> NormalizeChecks(IReadOnlyList<WizardPreflightCheckDto>? checks)
    {
        if (checks is null || checks.Count == 0)
        {
            return [];
        }

        var result = new List<WizardPreflightCheckDto>(Math.Min(MaxChecksPerRun, checks.Count));
        foreach (var check in checks.Take(MaxChecksPerRun))
        {
            var id = (check.Id ?? string.Empty).Trim();
            var label = (check.Label ?? string.Empty).Trim();
            var status = (check.Status ?? string.Empty).Trim().ToLowerInvariant();
            var message = (check.Message ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(id) || id.Length > 64)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(label) || label.Length > 128)
            {
                continue;
            }

            if (!AllowedStatuses.Contains(status))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                message = "-";
            }

            if (message.Length > 1024)
            {
                message = message[..1024];
            }

            result.Add(new WizardPreflightCheckDto(
                Id: id,
                Label: label,
                Status: status,
                Message: message));
        }

        return result;
    }

    private static WizardPreflightRunDto Map(WizardPreflightRun entity)
    {
        return new WizardPreflightRunDto(
            entity.Id,
            entity.Actor,
            entity.PassedCount,
            entity.TotalCount,
            entity.RanAtUtc,
            ParseChecks(entity.ChecksJson));
    }

    private static IReadOnlyList<WizardPreflightCheckDto> ParseChecks(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<WizardPreflightCheckDto>>(raw, JsonOptions);
            return parsed ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<int> TrimHistoryAsync(CancellationToken cancellationToken)
    {
        var idsToDelete = await dbContext.WizardPreflightRuns
            .AsNoTracking()
            .OrderByDescending(x => x.RanAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(MaxStoredRuns)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (idsToDelete.Count == 0)
        {
            return 0;
        }

        return await dbContext.WizardPreflightRuns
            .Where(x => idsToDelete.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
