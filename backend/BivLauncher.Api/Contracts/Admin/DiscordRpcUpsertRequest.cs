using System.ComponentModel.DataAnnotations;

namespace BivLauncher.Api.Contracts.Admin;

public sealed class DiscordRpcUpsertRequest
{
    public bool Enabled { get; set; } = true;

    [MaxLength(128)]
    public string AppId { get; set; } = string.Empty;

    [MaxLength(256)]
    public string DetailsText { get; set; } = string.Empty;

    [MaxLength(256)]
    public string StateText { get; set; } = string.Empty;

    [MaxLength(128)]
    public string LargeImageKey { get; set; } = string.Empty;

    [MaxLength(128)]
    public string LargeImageText { get; set; } = string.Empty;

    [MaxLength(128)]
    public string SmallImageKey { get; set; } = string.Empty;

    [MaxLength(128)]
    public string SmallImageText { get; set; } = string.Empty;
}
