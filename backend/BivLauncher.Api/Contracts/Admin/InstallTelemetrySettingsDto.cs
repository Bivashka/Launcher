namespace BivLauncher.Api.Contracts.Admin;

public sealed record InstallTelemetrySettingsDto(
    bool Enabled,
    DateTime? UpdatedAtUtc);
