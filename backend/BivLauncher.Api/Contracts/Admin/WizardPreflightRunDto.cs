namespace BivLauncher.Api.Contracts.Admin;

public sealed record WizardPreflightRunDto(
    Guid Id,
    string Actor,
    int PassedCount,
    int TotalCount,
    DateTime RanAtUtc,
    IReadOnlyList<WizardPreflightCheckDto> Checks);
