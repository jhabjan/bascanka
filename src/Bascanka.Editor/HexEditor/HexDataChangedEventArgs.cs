namespace Bascanka.Editor.HexEditor;

/// <summary>
/// Event arguments carrying information about a data change in the hex editor.
/// </summary>
public sealed class HexDataChangedEventArgs(long offset, byte oldValue, byte newValue) : EventArgs
{
	/// <summary>The offset of the changed byte.</summary>
	public long Offset { get; } = offset;

	/// <summary>The old byte value.</summary>
	public byte OldValue { get; } = oldValue;

	/// <summary>The new byte value.</summary>
	public byte NewValue { get; } = newValue;
}
