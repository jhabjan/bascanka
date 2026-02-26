namespace Bascanka.Core.Buffer;

/// <summary>
/// A node in the augmented red-black tree.  Each node stores a <see cref="Piece"/>
/// and is augmented with subtree-level statistics so that character-offset and
/// line-number look-ups run in O(log N).
/// </summary>
public sealed class RBNode(Piece piece, NodeColor color)
{
	/// <summary>The piece descriptor stored in this node.</summary>
	public Piece Piece = piece;

	/// <summary>Node color used for red-black balancing.</summary>
	public NodeColor Color = color;

	public RBNode? Left;
	public RBNode? Right;
	public RBNode? Parent;

	// ── Augmented fields ──────────────────────────────────────────────

	/// <summary>
	/// Total character count stored in the entire left subtree of this node.
	/// Updated on every structural change (insert, delete, rotation).
	/// </summary>
	public long LeftSubtreeLength;

	/// <summary>
	/// Total line-feed count stored in the entire left subtree of this node.
	/// Updated on every structural change (insert, delete, rotation).
	/// </summary>
	public long LeftSubtreeLineFeeds;

	public override string ToString() =>
		$"RBNode({Piece}, {Color}, LeftLen={LeftSubtreeLength}, LeftLF={LeftSubtreeLineFeeds})";
}
