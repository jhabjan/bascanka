namespace Bascanka.Editor.Tabs;

/// <summary>
/// Event arguments raised before a tab context menu is displayed, allowing
/// consumers to customise the menu.
/// </summary>
public sealed class TabContextMenuOpeningEventArgs(int index, ContextMenuStrip menu) : EventArgs
{
	/// <summary>Zero-based index of the tab that was right-clicked.</summary>
	public int Index { get; } = index;

	/// <summary>The context menu about to be shown.  Handlers may add items.</summary>
	public ContextMenuStrip Menu { get; } = menu;
}
