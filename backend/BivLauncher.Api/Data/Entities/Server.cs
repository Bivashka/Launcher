namespace BivLauncher.Api.Data.Entities;

public sealed class Server
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public int Port { get; set; } = 25565;
    public string MainJarPath { get; set; } = "minecraft_main.jar";
    public string RuProxyAddress { get; set; } = string.Empty;
    public int RuProxyPort { get; set; } = 25565;
    public string RuJarPath { get; set; } = "minecraft_ru.jar";
    public string IconKey { get; set; } = string.Empty;
    public string LoaderType { get; set; } = "vanilla";
    public string McVersion { get; set; } = "1.21.1";
    public string BuildId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Order { get; set; } = 100;

    public Profile? Profile { get; set; }
}
