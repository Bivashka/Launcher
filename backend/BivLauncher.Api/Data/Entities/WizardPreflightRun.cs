namespace BivLauncher.Api.Data.Entities;

public sealed class WizardPreflightRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Actor { get; set; } = string.Empty;
    public int PassedCount { get; set; }
    public int TotalCount { get; set; }
    public string ChecksJson { get; set; } = "[]";
    public DateTime RanAtUtc { get; set; } = DateTime.UtcNow;
}
