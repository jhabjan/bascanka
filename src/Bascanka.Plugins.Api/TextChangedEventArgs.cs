namespace Bascanka.Plugins.Api;

/// <summary>
/// Event arguments raised when the text of a buffer changes.
/// </summary>
/// <remarks>
/// Initializes a new instance of <see cref="TextChangedEventArgs"/>.
/// </remarks>
/// <param name="offset">The zero-based character offset where the change started.</param>
/// <param name="oldLength">The number of characters that were removed.</param>
/// <param name="newLength">The number of characters that were inserted.</param>
public class TextChangedEventArgs(long offset, long oldLength, long newLength) : EventArgs
{

	/// <summary>Gets the zero-based character offset where the change started.</summary>
	public long Offset { get; } = offset;

	/// <summary>Gets the number of characters that were removed.</summary>
	public long OldLength { get; } = oldLength;

	/// <summary>Gets the number of characters that were inserted.</summary>
	public long NewLength { get; } = newLength;
}
