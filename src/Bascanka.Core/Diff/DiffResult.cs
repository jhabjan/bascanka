namespace Bascanka.Core.Diff;

public sealed class DiffResult
{
    public DiffSide Left { get; init; } = new();
    public DiffSide Right { get; init; } = new();
    public int[] DiffSectionStarts { get; init; } = [];
    public int DiffCount { get; init; }
}
