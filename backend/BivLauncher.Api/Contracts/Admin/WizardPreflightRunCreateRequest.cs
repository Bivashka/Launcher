namespace BivLauncher.Api.Contracts.Admin;

public sealed record WizardPreflightRunCreateRequest(
    IReadOnlyList<WizardPreflightCheckDto> Checks);
