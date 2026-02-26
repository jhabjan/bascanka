namespace Bascanka.Editor.Panels;

/// <summary>
/// Model for a single tab in the bottom panel tab strip.
/// </summary>
public sealed class BottomPanelTab
{
	/// <summary>Unique identifier for the tab.</summary>
	public required string Id { get; init; }

	/// <summary>Display title shown on the tab.</summary>
	public string Title { get; set; } = string.Empty;

	/// <summary>The content control displayed when this tab is active.</summary>
	public required Control Content { get; init; }

	/// <summary>Whether this tab shows a close button.</summary>
	public bool Closable { get; init; }
}
