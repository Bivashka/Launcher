using BivLauncher.Client.Models;
using System.Text.Json;

namespace BivLauncher.Client.Services;

public sealed class PendingSubmissionService(ISettingsService settingsService) : IPendingSubmissionService
{
    private const int MaxQueueItems = 40;
    private const int MaxAttempts = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ISettingsService _settingsService = settingsService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task EnqueueCrashReportAsync(
        string apiBaseUrl,
        PublicCrashReportCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = new PendingSubmissionItem
        {
            Type = PendingSubmissionTypes.CrashReport,
            ApiBaseUrl = NormalizeApiBaseUrl(apiBaseUrl),
            CrashReport = CloneCrashReport(request),
            CreatedAtUtc = DateTime.UtcNow
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadStoreUnsafeAsync(cancellationToken);
            store.Items.Add(item);
            TrimToMaxItems(store.Items);
            await SaveStoreUnsafeAsync(store, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnqueueInstallTelemetryAsync(
        string apiBaseUrl,
        PublicInstallTelemetryTrackRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedApi = NormalizeApiBaseUrl(apiBaseUrl);
        var projectKey = request.ProjectName.Trim().ToLowerInvariant();
        var launcherVersion = request.LauncherVersion.Trim();

        var item = new PendingSubmissionItem
        {
            Type = PendingSubmissionTypes.InstallTelemetry,
            ApiBaseUrl = normalizedApi,
            InstallTelemetry = new PublicInstallTelemetryTrackRequest
            {
                ProjectName = request.ProjectName,
                LauncherVersion = request.LauncherVersion
            },
            CreatedAtUtc = DateTime.UtcNow
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadStoreUnsafeAsync(cancellationToken);

            store.Items.RemoveAll(existing =>
                existing.Type.Equals(PendingSubmissionTypes.InstallTelemetry, StringComparison.OrdinalIgnoreCase) &&
                existing.ApiBaseUrl.Equals(normalizedApi, StringComparison.OrdinalIgnoreCase) &&
                existing.InstallTelemetry is not null &&
                existing.InstallTelemetry.ProjectName.Trim().Equals(projectKey, StringComparison.OrdinalIgnoreCase) &&
                existing.InstallTelemetry.LauncherVersion.Trim().Equals(launcherVersion, StringComparison.Ordinal));

            store.Items.Add(item);
            TrimToMaxItems(store.Items);
            await SaveStoreUnsafeAsync(store, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PendingSubmissionFlushResult> FlushAsync(
        Func<PendingSubmissionItem, CancellationToken, Task<bool>> sender,
        CancellationToken cancellationToken = default)
    {
        if (sender is null)
        {
            throw new ArgumentNullException(nameof(sender));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadStoreUnsafeAsync(cancellationToken);
            if (store.Items.Count == 0)
            {
                return new PendingSubmissionFlushResult(0, 0, 0, 0);
            }

            var sent = 0;
            var failed = 0;
            var dropped = 0;
            var remaining = new List<PendingSubmissionItem>(store.Items.Count);

            var ordered = store.Items
                .OrderBy(x => x.CreatedAtUtc)
                .ToList();

            foreach (var item in ordered)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    remaining.Add(item);
                    continue;
                }

                if (item.AttemptCount >= MaxAttempts)
                {
                    dropped++;
                    continue;
                }

                if (!HasPayload(item))
                {
                    dropped++;
                    continue;
                }

                var delivered = false;
                try
                {
                    delivered = await sender(item, cancellationToken);
                }
                catch
                {
                    delivered = false;
                }

                if (delivered)
                {
                    sent++;
                    continue;
                }

                failed++;
                item.AttemptCount = Math.Max(0, item.AttemptCount) + 1;
                item.LastAttemptAtUtc = DateTime.UtcNow;
                remaining.Add(item);
            }

            var beforeTrim = remaining.Count;
            TrimToMaxItems(remaining);
            dropped += Math.Max(0, beforeTrim - remaining.Count);

            store.Items = remaining;
            await SaveStoreUnsafeAsync(store, cancellationToken);

            return new PendingSubmissionFlushResult(
                SentCount: sent,
                FailedCount: failed,
                DroppedCount: dropped,
                RemainingCount: remaining.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PendingSubmissionStore> LoadStoreUnsafeAsync(CancellationToken cancellationToken)
    {
        var path = GetStorePath();
        if (!File.Exists(path))
        {
            return new PendingSubmissionStore();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<PendingSubmissionStore>(stream, JsonOptions, cancellationToken);
            if (loaded?.Items is null)
            {
                return new PendingSubmissionStore();
            }

            loaded.Items = loaded.Items
                .Where(x => x is not null)
                .ToList();
            return loaded;
        }
        catch
        {
            return new PendingSubmissionStore();
        }
    }

    private async Task SaveStoreUnsafeAsync(PendingSubmissionStore store, CancellationToken cancellationToken)
    {
        var path = GetStorePath();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var tempPath = $"{path}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, store, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, true);
    }

    private string GetStorePath()
    {
        var settingsPath = _settingsService.GetSettingsFilePath();
        var root = Path.GetDirectoryName(settingsPath)!;
        return Path.Combine(root, "pending-submissions.json");
    }

    private static bool HasPayload(PendingSubmissionItem item)
    {
        return item.Type switch
        {
            PendingSubmissionTypes.CrashReport => item.CrashReport is not null,
            PendingSubmissionTypes.InstallTelemetry => item.InstallTelemetry is not null,
            _ => false
        };
    }

    private static void TrimToMaxItems(List<PendingSubmissionItem> items)
    {
        if (items.Count <= MaxQueueItems)
        {
            return;
        }

        var removeCount = items.Count - MaxQueueItems;
        items.Sort((left, right) => left.CreatedAtUtc.CompareTo(right.CreatedAtUtc));
        items.RemoveRange(0, removeCount);
    }

    private static string NormalizeApiBaseUrl(string? apiBaseUrl)
    {
        return (apiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
    }

    private static PublicCrashReportCreateRequest CloneCrashReport(PublicCrashReportCreateRequest request)
    {
        return new PublicCrashReportCreateRequest
        {
            ProfileSlug = request.ProfileSlug,
            ServerName = request.ServerName,
            RouteCode = request.RouteCode,
            LauncherVersion = request.LauncherVersion,
            OsVersion = request.OsVersion,
            JavaVersion = request.JavaVersion,
            ExitCode = request.ExitCode,
            Reason = request.Reason,
            ErrorType = request.ErrorType,
            LogExcerpt = request.LogExcerpt,
            OccurredAtUtc = request.OccurredAtUtc,
            Metadata = request.Metadata?.Clone()
        };
    }
}
