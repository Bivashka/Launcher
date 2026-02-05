namespace BivLauncher.Api.Contracts.Admin;

public sealed record ProjectInstallStatDto(
    Guid Id,
    string ProjectName,
    string LastLauncherVersion,
    int SeenCount,
    DateTime FirstSeenAtUtc,
    DateTime LastSeenAtUtc);
