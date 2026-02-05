namespace BivLauncher.Api.Contracts.Public;

public sealed record PublicInstallTelemetryTrackResponse(
    bool Accepted,
    bool Enabled,
    DateTime ProcessedAtUtc);
