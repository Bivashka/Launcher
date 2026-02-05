namespace BivLauncher.Api.Contracts.Admin;

public sealed record StorageMigrationResultDto(
    bool DryRun,
    bool SourceUseS3,
    bool TargetUseS3,
    int Scanned,
    int Copied,
    int Skipped,
    int Failed,
    long CopiedBytes,
    bool Truncated,
    long DurationMs,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    IReadOnlyList<string> Errors);
