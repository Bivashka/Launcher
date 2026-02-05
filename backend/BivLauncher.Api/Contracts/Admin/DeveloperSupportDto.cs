namespace BivLauncher.Api.Contracts.Admin;

public sealed record DeveloperSupportDto(
    string DisplayName,
    string Telegram,
    string Discord,
    string Website,
    string Notes);
