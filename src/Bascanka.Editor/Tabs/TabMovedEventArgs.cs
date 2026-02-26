namespace Bascanka.Editor.Tabs;

/// <summary>
/// Event arguments for the <see cref="TabDragManager.TabMoved"/> event.
/// </summary>
public sealed class TabMovedEventArgs(int fromIndex, int toIndex) : EventArgs
{
	/// <summary>Original index of the dragged tab.</summary>
	public int FromIndex { get; } = fromIndex;

	/// <summary>New index where the tab was dropped.</summary>
	public int ToIndex { get; } = toIndex;
}
