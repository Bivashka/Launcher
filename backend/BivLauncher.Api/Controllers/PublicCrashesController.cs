using BivLauncher.Api.Contracts.Public;
using BivLauncher.Api.Data;
using BivLauncher.Api.Data.Entities;
using BivLauncher.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BivLauncher.Api.Controllers;

[ApiController]
[EnableRateLimiting(RateLimitPolicies.PublicIngestPolicy)]
[Route("api/public/crashes")]
public sealed partial class PublicCrashesController(AppDbContext dbContext) : ControllerBase
{
    private const int MaxLogChars = 16384;
    private const int MaxLogLines = 200;
    private const int MaxLineChars = 512;
    private const int MaxMetadataChars = 4096;

    [HttpPost]
    public async Task<ActionResult<PublicCrashReportCreateResponse>> Create(
        [FromBody] PublicCrashReportCreateRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var occurredAtUtc = request.OccurredAtUtc?.ToUniversalTime() ?? now;
        if (occurredAtUtc > now.AddMinutes(5))
        {
            occurredAtUtc = now;
        }

        var sanitizedLog = NormalizeAndSanitizeLog(request.LogExcerpt);
        var errorType = TrimAndLimit(request.ErrorType, 128);
        var reason = TrimAndLimit(request.Reason, 512);
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = BuildReason(request.ExitCode, errorType);
        }

        if (string.IsNullOrWhiteSpace(errorType))
        {
            errorType = InferErrorType(reason, sanitizedLog);
        }

        var crash = new CrashReport
        {
            CrashId = BuildCrashId(now),
            Status = "new",
            ProfileSlug = TrimAndLimit(request.ProfileSlug, 64).ToLowerInvariant(),
            ServerName = TrimAndLimit(request.ServerName, 128),
            RouteCode = TrimAndLimit(request.RouteCode, 16).ToLowerInvariant(),
            LauncherVersion = TrimAndLimit(request.LauncherVersion, 64),
            OsVersion = TrimAndLimit(request.OsVersion, 128),
            JavaVersion = TrimAndLimit(request.JavaVersion, 128),
            ExitCode = request.ExitCode,
            Reason = reason,
            ErrorType = errorType,
            LogExcerpt = sanitizedLog,
            MetadataJson = BuildMetadataJson(request.Metadata),
            OccurredAtUtc = occurredAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.CrashReports.Add(crash);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new PublicCrashReportCreateResponse(crash.CrashId, crash.CreatedAtUtc));
    }

    private static string BuildCrashId(DateTime now)
    {
        return $"CR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }

    private static string BuildReason(int? exitCode, string errorType)
    {
        if (!string.IsNullOrWhiteSpace(errorType))
        {
            return $"Unhandled {errorType}.";
        }

        if (!exitCode.HasValue)
        {
            return "Launcher exception before process exit.";
        }

        if (exitCode.Value == 0)
        {
            return "Process finished with success code.";
        }

        return exitCode.Value switch
        {
            -1073740791 => "Process terminated by invalid Java/runtime arguments.",
            -1073741819 => "Process crashed with access violation.",
            _ => $"Process exited with code {exitCode.Value}."
        };
    }

    private static string InferErrorType(string reason, string logExcerpt)
    {
        var source = $"{reason} {logExcerpt}";
        if (source.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "Timeout";
        }

        if (source.Contains("auth", StringComparison.OrdinalIgnoreCase))
        {
            return "Auth";
        }

        if (source.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "MissingDependency";
        }

        if (source.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return "Network";
        }

        return "ProcessExit";
    }

    private static string NormalizeAndSanitizeLog(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var normalized = raw.Replace('\0', ' ').Replace("\r\n", "\n");
        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .TakeLast(MaxLogLines)
            .Select(x => RedactSecrets(TrimAndLimit(x, MaxLineChars)))
            .ToList();

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var joined = string.Join('\n', lines);
        return TrimAndLimit(joined, MaxLogChars);
    }

    private static string BuildMetadataJson(JsonElement? metadata)
    {
        if (metadata is null || metadata.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return "{}";
        }

        try
        {
            var sanitized = SanitizeMetadataValue(metadata.Value, depth: 0);
            var json = JsonSerializer.Serialize(sanitized);
            if (json.Length <= MaxMetadataChars)
            {
                return json;
            }

            return JsonSerializer.Serialize(new
            {
                truncated = true,
                originalLength = json.Length
            });
        }
        catch
        {
            return "{}";
        }
    }

    private static object? SanitizeMetadataValue(JsonElement element, int depth)
    {
        if (depth > 4)
        {
            return "<max-depth>";
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .Take(40)
                .ToDictionary(
                    x => TrimAndLimit(x.Name, 64),
                    x => SanitizeMetadataValue(x.Value, depth + 1)),
            JsonValueKind.Array => element.EnumerateArray()
                .Take(40)
                .Select(x => SanitizeMetadataValue(x, depth + 1))
                .ToList(),
            JsonValueKind.String => RedactSecrets(TrimAndLimit(element.GetString() ?? string.Empty, 256)),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string RedactSecrets(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var output = SensitivePairRegex().Replace(value, "$1=<redacted>");
        output = BearerRegex().Replace(output, "$1<redacted>");
        output = JwtRegex().Replace(output, "<jwt:redacted>");
        return output;
    }

    private static string TrimAndLimit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }

    [GeneratedRegex("(?i)\\b(password|passwd|pass|token|secret|authorization|access[_-]?token)\\b\\s*[:=]\\s*([^\\s,;]+)", RegexOptions.Compiled)]
    private static partial Regex SensitivePairRegex();

    [GeneratedRegex("(?i)\\b(bearer\\s+)[a-z0-9\\-_.=]+", RegexOptions.Compiled)]
    private static partial Regex BearerRegex();

    [GeneratedRegex("eyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}", RegexOptions.Compiled)]
    private static partial Regex JwtRegex();
}
