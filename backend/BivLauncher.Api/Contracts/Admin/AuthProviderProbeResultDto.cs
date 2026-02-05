namespace BivLauncher.Api.Contracts.Admin;

public sealed record AuthProviderProbeResultDto(
    bool Success,
    string AuthMode,
    string LoginUrl,
    int? StatusCode,
    string Message,
    DateTime CheckedAtUtc);
