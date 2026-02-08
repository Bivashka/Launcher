namespace BivLauncher.Api.Contracts.Public;

public sealed record PublicAuthSessionResponse(
    string Username,
    string ExternalId,
    IReadOnlyList<string> Roles);
