namespace BivLauncher.Api.Services;

public static class LoaderCatalog
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "vanilla",
        "forge",
        "fabric",
        "quilt",
        "neoforge",
        "liteloader"
    };

    public static IReadOnlyList<string> SupportedLoaders { get; } =
        Supported.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string NormalizeLoader(string? loaderType, string fallback = "vanilla")
    {
        if (string.IsNullOrWhiteSpace(loaderType))
        {
            return fallback;
        }

        return loaderType.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string loaderType)
    {
        return !string.IsNullOrWhiteSpace(loaderType) && Supported.Contains(loaderType);
    }
}
