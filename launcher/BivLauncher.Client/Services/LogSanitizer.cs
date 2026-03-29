using System.Text.RegularExpressions;

namespace BivLauncher.Client.Services;

internal static partial class LogSanitizer
{
    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteUrlRegex();

    public static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return AbsoluteUrlRegex().Replace(message, "[redacted-url]");
    }
}
