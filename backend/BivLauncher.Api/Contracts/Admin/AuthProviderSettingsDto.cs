namespace BivLauncher.Api.Contracts.Admin;

public sealed record AuthProviderSettingsDto(
    string LoginUrl,
    int TimeoutSeconds,
    bool AllowDevFallback,
    DateTime? UpdatedAtUtc);
