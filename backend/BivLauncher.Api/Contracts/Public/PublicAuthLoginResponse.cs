namespace BivLauncher.Api.Contracts.Public;

public sealed record PublicAuthLoginResponse(
    string Token,
    string TokenType,
    string Username,
    string ExternalId,
    IReadOnlyList<string> Roles);
