namespace BivLauncher.Client.Models;

public sealed class LocalizedOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }

    public override string ToString() => Label;
}
