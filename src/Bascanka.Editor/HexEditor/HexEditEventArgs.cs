namespace Bascanka.Editor.HexEditor;

/// <summary>
/// Event arguments for a single-byte edit in the hex renderer.
/// </summary>
public sealed class HexEditEventArgs(long offset, byte oldValue, byte newValue) : EventArgs
{
	/// <summary>The byte offset that was edited.</summary>
	public long Offset { get; } = offset;

	/// <summary>The old byte value before the edit.</summary>
	public byte OldValue { get; } = oldValue;

	/// <summary>The new byte value after the edit.</summary>
	public byte NewValue { get; } = newValue;
}
