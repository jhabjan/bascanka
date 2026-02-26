namespace Bascanka.Core.Buffer;

/// <summary>
/// Event arguments for the <see cref="PieceTable.TextChanged"/> event.
/// </summary>
public sealed class TextChangedEventArgs(long offset, long oldLength, long newLength) : EventArgs
{
	/// <summary>Character offset where the change starts.</summary>
	public long Offset { get; } = offset;

	/// <summary>Number of characters that were removed.</summary>
	public long OldLength { get; } = oldLength;

	/// <summary>Number of characters that were inserted.</summary>
	public long NewLength { get; } = newLength;
}
