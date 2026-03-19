using BivLauncher.Api.Data.Entities;

namespace BivLauncher.Api.Infrastructure;

public static class ProfileAccessRules
{
    private static readonly char[] UsernameSeparators = [',', ';', '\r', '\n', '\t', ' '];

    public static bool CanAccess(Profile profile, string? username)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return CanAccess(profile.IsPrivate, profile.AllowedPlayerUsernames, username);
    }

    public static bool CanAccess(bool isPrivate, string? allowedPlayerUsernames, string? username)
    {
        if (!isPrivate)
        {
            return true;
        }

        var normalizedUsername = NormalizePlayerUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            return false;
        }

        return ParseAllowedPlayerUsernames(allowedPlayerUsernames)
            .Contains(normalizedUsername, StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizeAllowedPlayerUsernames(string? rawUsernames)
    {
        return string.Join(
            '\n',
            ParseAllowedPlayerUsernames(rawUsernames));
    }

    public static IReadOnlyList<string> ParseAllowedPlayerUsernames(string? rawUsernames)
    {
        return (rawUsernames ?? string.Empty)
            .Split(UsernameSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePlayerUsername)
            .Where(static username => !string.IsNullOrWhiteSpace(username))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizePlayerUsername(string? username)
    {
        var normalized = (username ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }
}
