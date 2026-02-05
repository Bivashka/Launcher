namespace BivLauncher.Api.Contracts.Admin;

public sealed record WizardPreflightCheckDto(
    string Id,
    string Label,
    string Status,
    string Message);
