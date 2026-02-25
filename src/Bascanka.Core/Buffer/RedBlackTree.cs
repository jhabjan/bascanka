using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Bascanka.Core.Buffer;

/// <summary>
/// Node color in a red-black tree.
/// </summary>
public enum NodeColor : byte
{
    Red = 0,
    Black = 1,
}

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

/// <summary>
/// A red-black tree augmented for text editing.  Supports O(log N) look-up by
/// character offset and by line number, as well as standard insert and delete
/// with full rebalancing and augmentation maintenance.
/// </summary>
public sealed class RedBlackTree : IEnumerable<Piece>
{
    /// <summary>Sentinel nil node shared by the entire tree.</summary>
    internal readonly RBNode Nil;

    /// <summary>Root of the tree.  Points to <see cref="Nil"/> when empty.</summary>
    public RBNode Root;

    /// <summary>Number of nodes in the tree.</summary>
    public int Count { get; private set; }

    public RedBlackTree()
    {
        Nil = new RBNode(default, NodeColor.Black)
        {
            Left = null,
            Right = null,
            Parent = null,
        };
        // Nil's children/parent point to itself for safety.
        Nil.Left = Nil;
        Nil.Right = Nil;
        Nil.Parent = Nil;

        Root = Nil;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Public queries
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Total character length of the document represented by this tree.</summary>
    public long TotalLength
    {
        get
        {
            if (Root == Nil) return 0;
            return ComputeSubtreeLength(Root);
        }
    }

    /// <summary>Total line-feed count in the document represented by this tree.</summary>
    public long TotalLineFeeds
    {
        get
        {
            if (Root == Nil) return 0;
            return ComputeSubtreeLineFeeds(Root);
        }
    }

    /// <summary>
    /// Finds the node whose piece contains the given character <paramref name="offset"/>
    /// (zero-based) and returns the offset within that node's piece.
    /// </summary>
    /// <returns>
    /// A tuple of (node, offsetInNode).  If the tree is empty or the offset equals
    /// <see cref="TotalLength"/> (i.e. one past the end), the returned node is
    /// <see cref="Nil"/>.
    /// </returns>
    public (RBNode Node, long OffsetInNode) FindByOffset(long offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        RBNode current = Root;
        long remaining = offset;

        while (current is not null && current != Nil)
        {
            // Characters in left subtree
            long leftLen = current.LeftSubtreeLength;

            if (remaining < leftLen)
            {
                // Target is somewhere in the left subtree.
                current = current.Left!;
            }
            else if (remaining < leftLen + current.Piece.Length)
            {
                // Target is inside this node's piece.
                return (current, remaining - leftLen);
            }
            else
            {
                // Target is in the right subtree.
                remaining -= leftLen + current.Piece.Length;
                current = current.Right!;
            }
        }

        // offset == TotalLength (or tree is empty)
        return (Nil, 0);
    }

    /// <summary>
    /// Finds the node that contains the first character of the given
    /// <paramref name="lineNumber"/> (zero-based) and returns the offset within
    /// that node's piece where the line starts.
    /// </summary>
    /// <remarks>
    /// Line 0 starts at the very beginning of the document.  Line N starts
    /// immediately after the Nth <c>'\n'</c> in the document.
    /// </remarks>
    public (RBNode Node, long OffsetInNode) FindByLine(long lineNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineNumber);

        if (lineNumber == 0)
        {
            // Line 0 starts at the very first character.
            RBNode first = Minimum(Root);
            return (first, 0);
        }

        // We need to find the lineNumber-th '\n' and return the position
        // right after it.
        RBNode current = Root;
        long remainingLF = lineNumber; // number of '\n' to skip

        while (current is not null && current != Nil)
        {
            long leftLF = current.LeftSubtreeLineFeeds;

            if (remainingLF <= leftLF)
            {
                // The target line feed is in the left subtree.
                current = current.Left!;
            }
            else if (remainingLF <= leftLF + current.Piece.LineFeeds)
            {
                // The target line feed is inside this node's piece.
                // remainingLF - leftLF = which '\n' inside this piece (1-based)
                long lfInPiece = remainingLF - leftLF;
                return (current, lfInPiece); // caller must resolve within piece
            }
            else
            {
                remainingLF -= leftLF + current.Piece.LineFeeds;
                current = current.Right!;
            }
        }

        // Past the last line -- return Nil.
        return (Nil, 0);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Insertion
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inserts <paramref name="node"/> into the tree immediately after
    /// <paramref name="afterNode"/>.  If <paramref name="afterNode"/> is
    /// <see cref="Nil"/> the new node becomes the first (leftmost) node.
    /// </summary>
    public void InsertAfter(RBNode? afterNode, RBNode node)
    {
        node.Left = Nil;
        node.Right = Nil;
        node.Color = NodeColor.Red;
        node.LeftSubtreeLength = 0;
        node.LeftSubtreeLineFeeds = 0;

        if (Root == Nil)
        {
            Root = node;
            node.Parent = Nil;
            node.Color = NodeColor.Black;
            Count = 1;
            return;
        }

        if (afterNode == null || afterNode == Nil)
        {
            // Insert as the very first node (leftmost).
            RBNode leftmost = Minimum(Root);
            leftmost.Left = node;
            node.Parent = leftmost;
            UpdateAugmentationUp(leftmost);
        }
        else if (afterNode.Right is null || afterNode.Right == Nil)
        {
            afterNode.Right = node;
            node.Parent = afterNode;
            UpdateAugmentationUp(afterNode);
        }
        else
        {
            // afterNode has a right child; insert as leftmost of right subtree.
            RBNode successor = Minimum(afterNode.Right);
            successor.Left = node;
            node.Parent = successor;
            UpdateAugmentationUp(successor);
        }

        Count++;
        InsertFixup(node);
    }

    /// <summary>
    /// Creates a node for <paramref name="piece"/> and inserts it at the given
    /// character <paramref name="offset"/> in the logical text.  If the offset
    /// falls in the middle of an existing piece, that piece is split.
    /// </summary>
    /// <returns>The node(s) that were inserted.  Callers rarely need this.</returns>
    public RBNode InsertAtOffset(long offset, Piece piece)
    {
        if (Root == Nil)
        {
            var node = new RBNode(piece, NodeColor.Red);
            InsertAfter(null, node);
            return node;
        }

        if (offset >= TotalLength)
        {
            // Append at end.
            RBNode last = Maximum(Root);
            var node = new RBNode(piece, NodeColor.Red);
            InsertAfter(last, node);
            return node;
        }

        var (target, offsetInNode) = FindByOffset(offset);

        if (target == Nil)
        {
            // Should not happen if offset < TotalLength, but be safe.
            RBNode last = Maximum(Root);
            var node = new RBNode(piece, NodeColor.Red);
            InsertAfter(last, node);
            return node;
        }

        if (offsetInNode == 0)
        {
            // Insert before `target` => after predecessor.
            RBNode? pred = Predecessor(target);
            var node = new RBNode(piece, NodeColor.Red);
            InsertAfter(pred, node);
            return node;
        }

        // Need to split `target` at offsetInNode.
        // After the split we have: [leftPiece] [newPiece] [rightPiece]
        SplitNode(target, offsetInNode, out RBNode rightHalf);

        var inserted = new RBNode(piece, NodeColor.Red);
        InsertAfter(target, inserted);
        InsertAfter(inserted, rightHalf);

        return inserted;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Deletion
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Removes <paramref name="node"/> from the tree.</summary>
    public void Delete(RBNode node)
    {
        if (node == Nil)
            throw new InvalidOperationException("Cannot delete the sentinel node.");

        Count--;

        RBNode y = node;
        RBNode x;
        NodeColor yOriginalColor = y.Color;

        if (node.Left == Nil)
        {
            x = node.Right!;
            Transplant(node, node.Right!);
        }
        else if (node.Right == Nil)
        {
            x = node.Left!;
            Transplant(node, node.Left!);
        }
        else
        {
            // Node has two children -- replace with in-order successor.
            y = Minimum(node.Right!);
            yOriginalColor = y.Color;
            x = y.Right!;

            if (y.Parent == node)
            {
                x.Parent = y; // x might be Nil
            }
            else
            {
                Transplant(y, y.Right!);
                y.Right = node.Right;
                y.Right!.Parent = y;
            }

            Transplant(node, y);
            y.Left = node.Left;
            y.Left!.Parent = y;
            y.Color = node.Color;

            // Recompute augmentation for y since its children changed.
            RecomputeAugmentation(y);
        }

        // Propagate augmentation up from the point of structural change.
        if (x != Nil)
            UpdateAugmentationUp(x.Parent!);
        else if (y != Nil)
            UpdateAugmentationUp(y.Parent!);

        // Detach removed node.
        node.Left = Nil;
        node.Right = Nil;
        node.Parent = Nil;

        if (yOriginalColor == NodeColor.Black)
            DeleteFixup(x);
    }

    /// <summary>
    /// Deletes a range of characters from the tree, starting at character
    /// <paramref name="offset"/> and spanning <paramref name="length"/> characters.
    /// May remove whole nodes and/or shrink partial pieces at the boundaries.
    /// </summary>
    public void DeleteRange(long offset, long length)
    {
        if (length <= 0) return;
        if (offset < 0 || offset + length > TotalLength)
            throw new ArgumentOutOfRangeException(nameof(offset));

        while (length > 0 && Root != Nil)
        {
            var (node, offInNode) = FindByOffset(offset);
            if (node == Nil) break;

            long pieceLen = node.Piece.Length;
            long charsAvail = pieceLen - offInNode;
            long charsToRemove = Math.Min(charsAvail, length);

            if (offInNode == 0 && charsToRemove == pieceLen)
            {
                // Remove the whole node.
                Delete(node);
            }
            else if (offInNode == 0)
            {
                // Trim from the start of the piece.
                ShrinkPieceStart(node, charsToRemove);
            }
            else if (offInNode + charsToRemove == pieceLen)
            {
                // Trim from the end of the piece.
                ShrinkPieceEnd(node, offInNode);
            }
            else
            {
                // Remove from the middle -- split piece around the gap.
                // Keep [0..offInNode) and [offInNode+charsToRemove..pieceLen)
                SplitNode(node, offInNode, out RBNode rightHalf);

                // Now node covers [0..offInNode) and rightHalf covers
                // [offInNode..pieceLen). We need to trim charsToRemove from
                // the start of rightHalf.
                ShrinkPieceStart(rightHalf, charsToRemove);

                InsertAfter(node, rightHalf);
            }

            length -= charsToRemove;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Traversal
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Returns an in-order enumeration of all pieces in the tree.</summary>
    public IEnumerator<Piece> GetEnumerator()
    {
        var stack = new Stack<RBNode>();
        RBNode? current = Root;

        while ((current is not null && current != Nil) || stack.Count > 0)
        {
            while (current is not null && current != Nil)
            {
                stack.Push(current);
                current = current.Left;
            }

            current = stack.Pop();
            yield return current.Piece;
            current = current.Right;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns an in-order enumeration of all <see cref="RBNode"/> references.
    /// </summary>
    public IEnumerable<RBNode> InOrderNodes()
    {
        var stack = new Stack<RBNode>();
        RBNode? current = Root;

        while ((current is not null && current != Nil) || stack.Count > 0)
        {
            while (current is not null && current != Nil)
            {
                stack.Push(current);
                current = current.Left;
            }

            current = stack.Pop();
            yield return current;
            current = current.Right;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Augmentation helpers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recomputes the augmented fields of a single node from its immediate
    /// children.  Does <b>not</b> propagate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void RecomputeAugmentation(RBNode? node)
    {
        if (node is null || node == Nil) return;

        if (node.Left is null || node.Left == Nil)
        {
            node.LeftSubtreeLength = 0;
            node.LeftSubtreeLineFeeds = 0;
        }
        else
        {
            node.LeftSubtreeLength = ComputeSubtreeLength(node.Left);
            node.LeftSubtreeLineFeeds = ComputeSubtreeLineFeeds(node.Left);
        }
    }

    /// <summary>
    /// Recomputes augmented fields from <paramref name="node"/> all the way up
    /// to the root.
    /// </summary>
    public void UpdateAugmentationUp(RBNode? node)
    {
        while (node != null && node != Nil)
        {
            RecomputeAugmentation(node);
            node = node.Parent;
        }
    }

    /// <summary>
    /// Total character length stored in the subtree rooted at <paramref name="node"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long ComputeSubtreeLength(RBNode? node)
    {
        if (node is null || node == Nil) return 0;

        return node.LeftSubtreeLength + node.Piece.Length + ComputeSubtreeLength(node.Right);
    }

    /// <summary>
    /// Total line-feed count stored in the subtree rooted at <paramref name="node"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long ComputeSubtreeLineFeeds(RBNode? node)
    {
        if (node is null || node == Nil) return 0;

        return node.LeftSubtreeLineFeeds + node.Piece.LineFeeds + ComputeSubtreeLineFeeds(node.Right);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Tree navigation
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Returns the leftmost node in the subtree rooted at <paramref name="node"/>.</summary>
    public RBNode Minimum(RBNode node)
    {
        while (node.Left is not null && node.Left != Nil)
            node = node.Left;
        return node;
    }

    /// <summary>Returns the rightmost node in the subtree rooted at <paramref name="node"/>.</summary>
    public RBNode Maximum(RBNode node)
    {
        while (node.Right is not null && node.Right != Nil)
            node = node.Right;
        return node;
    }

    /// <summary>Returns the in-order predecessor of <paramref name="node"/>, or <see cref="Nil"/>.</summary>
    public RBNode? Predecessor(RBNode node)
    {
        if (node.Left is not null && node.Left != Nil)
            return Maximum(node.Left);

        RBNode? y = node.Parent;
        while (y is not null && y != Nil && node == y.Left)
        {
            node = y;
            y = y.Parent;
        }

        return y;
    }

    /// <summary>Returns the in-order successor of <paramref name="node"/>, or <see cref="Nil"/>.</summary>
    public RBNode Successor(RBNode node)
    {
        if (node.Right is not null && node.Right != Nil)
            return Minimum(node.Right);

        RBNode? y = node.Parent;
        while (y is not null && y != Nil && node == y.Right)
        {
            node = y;
            y = y.Parent;
        }

        return y ?? Nil;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Rotations (maintain augmentation)
    // ════════════════════════════════════════════════════════════════════

    private void RotateLeft(RBNode x)
    {
        RBNode y = x.Right!;
        x.Right = y.Left;

        if (y.Left != Nil)
            y.Left!.Parent = x;

        y.Parent = x.Parent;

        if (x.Parent == Nil)
            Root = y;
        else if (x == x.Parent!.Left)
            x.Parent.Left = y;
        else
            x.Parent.Right = y;

        y.Left = x;
        x.Parent = y;

        // Fix augmentation -- x is now a child of y.
        RecomputeAugmentation(x);
        RecomputeAugmentation(y);

        // Propagate upward to keep ancestors correct.
        UpdateAugmentationUp(y.Parent);
    }

    private void RotateRight(RBNode y)
    {
        RBNode x = y.Left!;
        y.Left = x.Right;

        if (x.Right != Nil)
            x.Right!.Parent = y;

        x.Parent = y.Parent;

        if (y.Parent == Nil)
            Root = x;
        else if (y == y.Parent!.Left)
            y.Parent.Left = x;
        else
            y.Parent.Right = x;

        x.Right = y;
        y.Parent = x;

        // Fix augmentation -- y is now a child of x.
        RecomputeAugmentation(y);
        RecomputeAugmentation(x);

        UpdateAugmentationUp(x.Parent);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Insert fixup
    // ════════════════════════════════════════════════════════════════════

    private void InsertFixup(RBNode z)
    {
        while (z.Parent!.Color == NodeColor.Red)
        {
            if (z.Parent == z.Parent.Parent!.Left)
            {
                RBNode y = z.Parent.Parent.Right!;
                if (y.Color == NodeColor.Red)
                {
                    // Case 1
                    z.Parent.Color = NodeColor.Black;
                    y.Color = NodeColor.Black;
                    z.Parent.Parent.Color = NodeColor.Red;
                    z = z.Parent.Parent;
                }
                else
                {
                    if (z == z.Parent.Right)
                    {
                        // Case 2
                        z = z.Parent;
                        RotateLeft(z);
                    }
                    // Case 3
                    z.Parent!.Color = NodeColor.Black;
                    z.Parent.Parent!.Color = NodeColor.Red;
                    RotateRight(z.Parent.Parent);
                }
            }
            else
            {
                // Mirror of above with left/right swapped.
                RBNode y = z.Parent.Parent.Left!;
                if (y.Color == NodeColor.Red)
                {
                    z.Parent.Color = NodeColor.Black;
                    y.Color = NodeColor.Black;
                    z.Parent.Parent.Color = NodeColor.Red;
                    z = z.Parent.Parent;
                }
                else
                {
                    if (z == z.Parent.Left)
                    {
                        z = z.Parent;
                        RotateRight(z);
                    }
                    z.Parent!.Color = NodeColor.Black;
                    z.Parent.Parent!.Color = NodeColor.Red;
                    RotateLeft(z.Parent.Parent);
                }
            }

            if (z == Root) break;
        }

        Root.Color = NodeColor.Black;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Delete fixup
    // ════════════════════════════════════════════════════════════════════

    private void DeleteFixup(RBNode x)
    {
        while (x != Root && x.Color == NodeColor.Black)
        {
            if (x == x.Parent!.Left)
            {
                RBNode w = x.Parent.Right!;

                if (w.Color == NodeColor.Red)
                {
                    // Case 1
                    w.Color = NodeColor.Black;
                    x.Parent.Color = NodeColor.Red;
                    RotateLeft(x.Parent);
                    w = x.Parent.Right!;
                }

                if (w.Left!.Color == NodeColor.Black && w.Right!.Color == NodeColor.Black)
                {
                    // Case 2
                    w.Color = NodeColor.Red;
                    x = x.Parent;
                }
                else
                {
                    if (w.Right!.Color == NodeColor.Black)
                    {
                        // Case 3
                        w.Left!.Color = NodeColor.Black;
                        w.Color = NodeColor.Red;
                        RotateRight(w);
                        w = x.Parent!.Right!;
                    }
                    // Case 4
                    w.Color = x.Parent!.Color;
                    x.Parent.Color = NodeColor.Black;
                    w.Right!.Color = NodeColor.Black;
                    RotateLeft(x.Parent);
                    x = Root;
                }
            }
            else
            {
                // Mirror
                RBNode w = x.Parent.Left!;

                if (w.Color == NodeColor.Red)
                {
                    w.Color = NodeColor.Black;
                    x.Parent.Color = NodeColor.Red;
                    RotateRight(x.Parent);
                    w = x.Parent.Left!;
                }

                if (w.Right!.Color == NodeColor.Black && w.Left!.Color == NodeColor.Black)
                {
                    w.Color = NodeColor.Red;
                    x = x.Parent;
                }
                else
                {
                    if (w.Left!.Color == NodeColor.Black)
                    {
                        w.Right!.Color = NodeColor.Black;
                        w.Color = NodeColor.Red;
                        RotateLeft(w);
                        w = x.Parent!.Left!;
                    }
                    w.Color = x.Parent!.Color;
                    x.Parent.Color = NodeColor.Black;
                    w.Left!.Color = NodeColor.Black;
                    RotateRight(x.Parent);
                    x = Root;
                }
            }
        }

        x.Color = NodeColor.Black;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Transplant
    // ════════════════════════════════════════════════════════════════════

    private void Transplant(RBNode u, RBNode v)
    {
        if (u.Parent == Nil)
            Root = v;
        else if (u == u.Parent!.Left)
            u.Parent.Left = v;
        else
            u.Parent.Right = v;

        v.Parent = u.Parent;

        // Propagate augmentation from the point of change.
        if (u.Parent != Nil)
            UpdateAugmentationUp(u.Parent);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Piece manipulation helpers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Splits <paramref name="node"/>'s piece at <paramref name="offset"/>.
    /// After the call, <paramref name="node"/> covers <c>[0..offset)</c> and
    /// <paramref name="rightHalf"/> (a detached node) covers
    /// <c>[offset..Length)</c>.
    /// <para>
    /// <paramref name="rightHalf"/> is <b>not</b> inserted into the tree;
    /// the caller is responsible for that.
    /// </para>
    /// </summary>
    internal void SplitNode(RBNode node, long offset, out RBNode rightHalf)
    {
        Debug.Assert(offset > 0 && offset < node.Piece.Length);

        Piece orig = node.Piece;
        long leftLen = offset;
        long rightLen = orig.Length - offset;

        // We cannot cheaply compute exact line-feed counts for the two halves
        // without access to the underlying text buffers.  The PieceTable layer
        // provides a callback to recompute these after splits.
        // For now, we set them to -1 as a marker that they need recomputation.
        // The PieceTable.SplitPiece method will fix these up.

        Piece leftPiece = new(orig.BufferType, orig.Start, leftLen, -1);
        Piece rightPiece = new(orig.BufferType, orig.Start + leftLen, rightLen, -1);

        node.Piece = leftPiece;
        RecomputeAugmentation(node);
        UpdateAugmentationUp(node.Parent);

        rightHalf = new RBNode(rightPiece, NodeColor.Red);
    }

    /// <summary>
    /// Shrinks a piece by removing <paramref name="count"/> characters from its
    /// start (advances <c>Start</c> and decreases <c>Length</c>).
    /// Line-feed count is set to -1 and must be fixed up by the caller.
    /// </summary>
    internal void ShrinkPieceStart(RBNode node, long count)
    {
        Debug.Assert(count > 0 && count < node.Piece.Length);
        Piece p = node.Piece;
        node.Piece = new Piece(p.BufferType, p.Start + count, p.Length - count, -1);
        RecomputeAugmentation(node);
        UpdateAugmentationUp(node.Parent);
    }

    /// <summary>
    /// Truncates a piece so it only covers <c>[0..<paramref name="newLength"/>)</c>.
    /// Line-feed count is set to -1 and must be fixed up by the caller.
    /// </summary>
    internal void ShrinkPieceEnd(RBNode node, long newLength)
    {
        Debug.Assert(newLength > 0 && newLength < node.Piece.Length);
        Piece p = node.Piece;
        node.Piece = new Piece(p.BufferType, p.Start, newLength, -1);
        RecomputeAugmentation(node);
        UpdateAugmentationUp(node.Parent);
    }
}
