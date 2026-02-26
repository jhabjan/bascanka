namespace Bascanka.Editor.Macros;

/// <summary>
/// A recorded macro consisting of a named sequence of <see cref="MacroAction"/>
/// instances, optionally bound to a keyboard shortcut.
/// </summary>
public sealed class Macro
{
	/// <summary>Human-readable name for the macro.</summary>
	public string Name { get; set; } = "Untitled Macro";

	/// <summary>
	/// An optional keyboard shortcut string (e.g. <c>"Ctrl+Shift+M"</c>).
	/// <see langword="null"/> means no shortcut is assigned.
	/// </summary>
	public string? ShortcutKey { get; set; }

	/// <summary>The ordered list of actions that comprise this macro.</summary>
	public List<MacroAction> Actions { get; set; } = [];

	/// <summary>The date and time this macro was created.</summary>
	public DateTime Created { get; set; } = DateTime.Now;

	public override string ToString() =>
		$"{Name} ({Actions.Count} action{(Actions.Count == 1 ? "" : "s")})";
}
