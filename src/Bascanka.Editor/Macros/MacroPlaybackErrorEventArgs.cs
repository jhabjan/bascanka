namespace Bascanka.Editor.Macros;

/// <summary>
/// Information about an error that occurred while executing a macro action.
/// </summary>
public sealed class MacroPlaybackErrorEventArgs(MacroAction action, int actionIndex, Exception exception) : EventArgs
{
	/// <summary>The action that caused the error.</summary>
	public MacroAction Action { get; } = action;

	/// <summary>Zero-based index of the action within the macro.</summary>
	public int ActionIndex { get; } = actionIndex;

	/// <summary>The exception that was thrown.</summary>
	public Exception Exception { get; } = exception;
}
