namespace BivLauncher.Api.Infrastructure;

public static class LauncherVersionParser
{
    public static bool TryExtractClientVersion(
        string? headerValue,
        string requiredPrefix,
        out string rawVersion,
        out Version version)
    {
        rawVersion = string.Empty;
        version = new Version(0, 0, 0, 0);

        if (string.IsNullOrWhiteSpace(headerValue) ||
            string.IsNullOrWhiteSpace(requiredPrefix) ||
            !headerValue.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        rawVersion = headerValue[requiredPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        return TryParseComparableVersion(rawVersion, out version);
    }

    public static bool TryParseComparableVersion(string? rawVersion, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var normalized = rawVersion.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..].Trim();
        }

        var plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
        {
            normalized = normalized[..plusIndex];
        }

        var dashIndex = normalized.IndexOf('-');
        if (dashIndex >= 0)
        {
            normalized = normalized[..dashIndex];
        }

        var segments = normalized
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 4)
        {
            return false;
        }

        Span<int> parsedSegments = stackalloc int[4];
        var parsedCount = 0;

        foreach (var segment in segments)
        {
            var digitCount = 0;
            while (digitCount < segment.Length && char.IsDigit(segment[digitCount]))
            {
                digitCount++;
            }

            if (digitCount == 0 || !int.TryParse(segment[..digitCount], out var parsedSegment) || parsedSegment < 0)
            {
                return false;
            }

            parsedSegments[parsedCount++] = parsedSegment;
        }

        while (parsedCount < 4)
        {
            parsedSegments[parsedCount++] = 0;
        }

        version = new Version(
            parsedSegments[0],
            parsedSegments[1],
            parsedSegments[2],
            parsedSegments[3]);
        return true;
    }
}
