namespace BivLauncher.Api.Services;

public interface IExternalAuthService
{
    Task<ExternalAuthResult> AuthenticateAsync(
        string username,
        string password,
        string hwidHash,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalAuthResult
{
    public bool Success { get; init; }
    public string ExternalId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public List<string> Roles { get; init; } = ["player"];
    public bool Banned { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}
