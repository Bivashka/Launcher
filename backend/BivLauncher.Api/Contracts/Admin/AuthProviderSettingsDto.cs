namespace BivLauncher.Api.Contracts.Admin;

public sealed record AuthProviderSettingsDto(
    string AuthMode,
    string LoginUrl,
    string LoginFieldKey,
    string PasswordFieldKey,
    int TimeoutSeconds,
    bool AllowDevFallback,
    DateTime? UpdatedAtUtc);
