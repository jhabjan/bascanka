namespace Bascanka.Core.Diff;

public readonly struct CharDiffRange(int start, int length)
{
	public int Start { get; } = start;
	public int Length { get; } = length;
}
