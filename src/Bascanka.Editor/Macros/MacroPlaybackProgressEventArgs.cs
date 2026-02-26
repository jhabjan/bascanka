namespace Bascanka.Editor.Macros;

/// <summary>
/// Progress information emitted after each macro action is executed.
/// </summary>
public sealed class MacroPlaybackProgressEventArgs(MacroAction action, int executedCount, int totalCount, long caretOffset) : EventArgs
{
	/// <summary>The action that was just executed.</summary>
	public MacroAction Action { get; } = action;

	/// <summary>Number of actions executed so far.</summary>
	public int ExecutedCount { get; } = executedCount;

	/// <summary>Total number of actions in the playback session.</summary>
	public int TotalCount { get; } = totalCount;

	/// <summary>The caret offset after execution.</summary>
	public long CaretOffset { get; } = caretOffset;
}
