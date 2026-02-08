namespace BivLauncher.Client.Models;

public sealed class GameLaunchRoute
{
    public string RouteCode { get; init; } = "main";
    public string Address { get; init; } = string.Empty;
    public int Port { get; init; } = 25565;
    public string PreferredJarPath { get; init; } = string.Empty;
    public string McVersion { get; init; } = string.Empty;
}
