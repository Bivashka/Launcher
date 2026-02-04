namespace BivLauncher.Api.Data.Entities;

public sealed class SkinAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public AuthAccount? Account { get; set; }
}
