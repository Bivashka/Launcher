using System.Collections.Concurrent;

namespace BivLauncher.Api.Infrastructure;

/// <summary>
/// Short-lived cache of launcher-validated sessions used as fallback
/// for legacy clients that skip explicit join call.
/// </summary>
public static class LegacyLauncherSessionCache
{
    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(3);

    public static void Touch(string username, int sessionVersion, DateTime utcNow)
    {
        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername) || sessionVersion < 0)
        {
            return;
        }

        Entries[normalizedUsername] = new Entry(
            SessionVersion: sessionVersion,
            ExpiresAtUtc: utcNow.Add(EntryLifetime));
        PruneExpired(utcNow);
    }

    public static bool TryGet(string username, DateTime utcNow, out int sessionVersion)
    {
        sessionVersion = 0;
        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            return false;
        }

        if (!Entries.TryGetValue(normalizedUsername, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAtUtc <= utcNow)
        {
            Entries.TryRemove(normalizedUsername, out _);
            return false;
        }

        sessionVersion = entry.SessionVersion;
        return true;
    }

    private static void PruneExpired(DateTime utcNow)
    {
        foreach (var (username, entry) in Entries)
        {
            if (entry.ExpiresAtUtc > utcNow)
            {
                continue;
            }

            Entries.TryRemove(username, out _);
        }
    }

    private static string NormalizeUsername(string username)
    {
        return (username ?? string.Empty).Trim().ToLowerInvariant();
    }

    private sealed record Entry(int SessionVersion, DateTime ExpiresAtUtc);
}
