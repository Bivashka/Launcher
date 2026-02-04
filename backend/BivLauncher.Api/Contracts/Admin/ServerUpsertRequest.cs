using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class ServerUpsertRequest
{
    [Required]
    public Guid ProfileId { get; set; }

    [Required]
    [MinLength(2)]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    [MaxLength(255)]
    public string Address { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 25565;

    [MaxLength(512)]
    public string MainJarPath { get; set; } = "minecraft_main.jar";

    [MaxLength(255)]
    public string RuProxyAddress { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int RuProxyPort { get; set; } = 25565;

    [MaxLength(512)]
    public string RuJarPath { get; set; } = "minecraft_ru.jar";

    [MaxLength(512)]
    public string IconKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string LoaderType { get; set; } = "vanilla";

    [Required]
    [MaxLength(32)]
    public string McVersion { get; set; } = "1.21.1";

    [MaxLength(64)]
    public string BuildId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    [Range(0, 10000)]
    public int Order { get; set; } = 100;
}
