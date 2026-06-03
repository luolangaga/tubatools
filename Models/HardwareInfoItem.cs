namespace TubaWinUi3.Models;

public sealed class HardwareInfoItem
{
    public required string Label { get; init; }

    public required string Value { get; init; }

    public string? BrandKey { get; set; }
}
