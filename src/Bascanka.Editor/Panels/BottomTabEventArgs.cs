namespace Bascanka.Editor.Panels;

/// <summary>
/// Event arguments carrying a tab identifier.
/// </summary>
public sealed class BottomTabEventArgs(string tabId) : EventArgs
{
	public string TabId { get; } = tabId;
}
