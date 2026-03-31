namespace BivLauncher.Api.Contracts.Public;

public sealed class PublicGameSessionStartRequest
{
    public Guid? ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
}
