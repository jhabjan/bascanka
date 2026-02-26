namespace Bascanka.Core.Diff;

public sealed class DiffSide
{
	public string Title { get; init; } = string.Empty;
	public string PaddedText { get; init; } = string.Empty;
	public DiffLine[] Lines { get; init; } = [];
}
