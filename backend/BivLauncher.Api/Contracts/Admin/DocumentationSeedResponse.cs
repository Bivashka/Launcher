namespace BivLauncher.Api.Contracts.Admin;

public sealed record DocumentationSeedResponse(
    int Inserted,
    int Skipped,
    IReadOnlyList<string> Slugs);
