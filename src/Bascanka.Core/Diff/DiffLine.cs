using static Enums;

namespace Bascanka.Core.Diff;

public sealed class DiffLine
{
	public DiffLineType Type { get; init; }
	public string Text { get; init; } = string.Empty;
	public int OriginalLineNumber { get; init; } // -1 for padding
	public List<CharDiffRange>? CharDiffs { get; init; }
}
