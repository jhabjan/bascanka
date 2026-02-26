namespace Bascanka.Editor.Tabs;

/// <summary>
/// Event arguments for tab-related events that carry an index.
/// </summary>
public sealed class TabEventArgs(int index) : EventArgs
{
	/// <summary>Zero-based index of the affected tab.</summary>
	public int Index { get; } = index;
}
