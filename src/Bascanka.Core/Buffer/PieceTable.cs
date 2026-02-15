using System.Text;

namespace Bascanka.Core.Buffer;

/// <summary>
/// Event arguments for the <see cref="PieceTable.TextChanged"/> event.
/// </summary>
public sealed class TextChangedEventArgs : EventArgs
{
    /// <summary>Character offset where the change starts.</summary>
    public long Offset { get; }

    /// <summary>Number of characters that were removed.</summary>
    public long OldLength { get; }

    /// <summary>Number of characters that were inserted.</summary>
    public long NewLength { get; }

    public TextChangedEventArgs(long offset, long oldLength, long newLength)
    {
        Offset = offset;
        OldLength = oldLength;
        NewLength = newLength;
    }
}

/// <summary>
/// A piece-table-based text buffer backed by an augmented red-black tree.
/// <para>
/// The buffer is composed of two underlying stores:
/// <list type="bullet">
///   <item>
///     <description>
///       An <see cref="ITextSource"/> that holds the immutable original text.
///     </description>
///   </item>
///   <item>
///     <description>
///       An append-only <see cref="StringBuilder"/> (<c>addBuffer</c>) that
///       accumulates every string ever inserted.
///     </description>
///   </item>
/// </list>
/// Each node in the red-black tree describes a contiguous span (a "piece")
/// in one of these two buffers.
/// </para>
/// </summary>
public sealed class PieceTable : IDisposable
{
    private readonly ITextSource _original;
    private readonly StringBuilder _addBuffer;
    private readonly RedBlackTree _tree;
    private bool _disposed;

    // ── Line-offset cache ────────────────────────────────────────────
    //
    // Maps every line index to its start offset in the document.
    // Built lazily with a single O(N) scan on first access after an
    // edit.  Invalidated on Insert / Delete.  Makes GetLineStartOffset
    // O(1) and OffsetToLineColumn O(log LineCount) via binary search.
    private long[]? _lineOffsetCache;

    /// <summary>
    /// Raised after every <see cref="Insert"/> or <see cref="Delete"/>
    /// operation.
    /// </summary>
    public event EventHandler<TextChangedEventArgs>? TextChanged;

    // ────────────────────────────────────────────────────────────────────
    //  Construction
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="PieceTable"/> from the given original text
    /// source.  The source is treated as immutable; all future edits go into
    /// the internal add buffer.
    /// </summary>
    public PieceTable(ITextSource original)
    {
        _original = original ?? throw new ArgumentNullException(nameof(original));
        _addBuffer = new StringBuilder();
        _tree = new RedBlackTree();

        if (_original.Length > 0)
        {
            int lf;
            if (_original is IPrecomputedLineFeeds precomputed)
            {
                lf = precomputed.InitialLineFeedCount;

                // Adopt the pre-built line-offset cache so that the first
                // GetLineStartOffset call doesn't trigger a full O(N) scan.
                if (precomputed.LineOffsets is { } offsets)
                    _lineOffsetCache = offsets;
            }
            else
            {
                lf = _original.CountLineFeeds(0, _original.Length);
            }

            var piece = new Piece(BufferType.Original, 0, _original.Length, lf);
            _tree.InsertAtOffset(0, piece);
        }
    }

    /// <summary>
    /// Convenience constructor that wraps a plain string.
    /// </summary>
    public PieceTable(string text)
        : this(new StringTextSource(text ?? string.Empty))
    {
    }

    // ────────────────────────────────────────────────────────────────────
    //  Properties
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Total number of characters in the document.</summary>
    public long Length => _tree.TotalLength;

    /// <summary>
    /// Number of lines in the document.  A document with no line-feeds has
    /// exactly one line.  Each <c>'\n'</c> adds one additional line.
    /// </summary>
    public long LineCount => _tree.TotalLineFeeds + 1;

    // ────────────────────────────────────────────────────────────────────
    //  Edit operations
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts <paramref name="text"/> at the given character
    /// <paramref name="offset"/> (zero-based).
    /// </summary>
    public void Insert(long offset, string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (text.Length == 0) return;
        if (offset < 0 || offset > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        // Normalize line endings to \n (internal representation).
        // Clipboard paste and plugin APIs may supply \r\n or bare \r.
        if (text.Contains('\r'))
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Append the new text to the add buffer.
        long addStart = _addBuffer.Length;
        _addBuffer.Append(text);

        int lf = CountLineFeedsInString(text);
        var piece = new Piece(BufferType.Add, addStart, text.Length, lf);

        _tree.InsertAtOffset(offset, piece);

        // After tree mutations the split pieces may have LineFeeds == -1.
        FixupLineFeeds();

        // Incrementally update the line-offset cache instead of full rebuild.
        UpdateLineOffsetCache(offset, 0, text.Length, text);

        TextChanged?.Invoke(this, new TextChangedEventArgs(offset, 0, text.Length));
    }

    /// <summary>
    /// Deletes <paramref name="length"/> characters starting at
    /// <paramref name="offset"/>.
    /// </summary>
    public void Delete(long offset, long length)
    {
        if (length == 0) return;
        if (offset < 0 || length < 0 || offset + length > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        // Count newlines in the range being deleted BEFORE modifying the tree,
        // so we can incrementally update the cache.
        // (UpdateLineOffsetCache will use the cache itself to count removed lines.)
        _tree.DeleteRange(offset, length);

        FixupLineFeeds();

        // Incrementally update the line-offset cache instead of full rebuild.
        UpdateLineOffsetCache(offset, length, 0, null);

        TextChanged?.Invoke(this, new TextChangedEventArgs(offset, length, 0));
    }

    // ────────────────────────────────────────────────────────────────────
    //  Read operations
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a substring of the document starting at <paramref name="offset"/>
    /// with the given <paramref name="length"/>.
    /// </summary>
    public string GetText(long offset, long length)
    {
        if (length == 0) return string.Empty;
        if (offset < 0 || length < 0 || offset + length > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var sb = new StringBuilder((int)Math.Min(length, int.MaxValue));
        long remaining = length;
        long pos = offset;

        while (remaining > 0)
        {
            var (node, offInNode) = _tree.FindByOffset(pos);
            if (node == _tree.Nil) break;

            long charsAvail = node.Piece.Length - offInNode;
            long take = Math.Min(charsAvail, remaining);

            AppendPieceText(sb, node.Piece, offInNode, take);

            pos += take;
            remaining -= take;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the character at the given <paramref name="offset"/>.
    /// </summary>
    public char GetCharAt(long offset)
    {
        if (offset < 0 || offset >= Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var (node, offInNode) = _tree.FindByOffset(offset);
        if (node == _tree.Nil)
            throw new InvalidOperationException("Offset unexpectedly resolved to Nil.");

        long bufferIndex = node.Piece.Start + offInNode;
        return node.Piece.BufferType == BufferType.Original
            ? _original[bufferIndex]
            : _addBuffer[(int)bufferIndex];
    }

    /// <summary>
    /// Returns the text of the line at the given zero-based
    /// <paramref name="lineIndex"/>.  The returned string does <b>not</b>
    /// include the terminating <c>'\n'</c>, if any.
    /// </summary>
    public string GetLine(long lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= LineCount)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));

        long lineStart = GetLineStartOffset(lineIndex);
        long lineEnd;

        if (lineIndex + 1 < LineCount)
        {
            // lineEnd = start of next line - 1  (to exclude the '\n').
            lineEnd = GetLineStartOffset(lineIndex + 1) - 1;
        }
        else
        {
            lineEnd = Length;
        }

        long len = lineEnd - lineStart;
        if (len <= 0) return string.Empty;

        return GetText(lineStart, len);
    }

    /// <summary>
    /// Returns the entire document text.
    /// </summary>
    public override string ToString() => Length == 0 ? string.Empty : GetText(0, Length);

    // ────────────────────────────────────────────────────────────────────
    //  Line helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the character offset where the given zero-based line starts.
    /// Uses the lazily-built line-offset cache for O(1) lookup.
    /// </summary>
    public long GetLineStartOffset(long lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= LineCount)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));

        if (lineIndex == 0) return 0;

        EnsureLineOffsetCache();
        return _lineOffsetCache![lineIndex];
    }

    // ────────────────────────────────────────────────────────────────────
    //  Line / column helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the text and start offset for a range of consecutive lines
    /// in a single efficient pass.  Uses two tree lookups plus one bulk
    /// <see cref="GetText"/> call instead of multiple per-line lookups,
    /// making it dramatically faster for rendering visible lines.
    /// </summary>
    /// <param name="startLine">Zero-based first line index.</param>
    /// <param name="count">Number of consecutive lines to retrieve.</param>
    /// <returns>
    /// An array of tuples where each element contains the line text
    /// (excluding the terminating <c>'\n'</c>) and the character offset
    /// where that line starts in the document.
    /// </returns>
    public (string Text, long StartOffset)[] GetLineRange(long startLine, int count)
    {
        if (count <= 0 || startLine < 0 || startLine >= LineCount)
            return [];

        long endLine = Math.Min(startLine + count, LineCount);
        int actualCount = (int)(endLine - startLine);

        long firstOffset = GetLineStartOffset(startLine);

        long lastOffset;
        if (endLine < LineCount)
            lastOffset = GetLineStartOffset(endLine);
        else
            lastOffset = Length;

        long totalLen = lastOffset - firstOffset;

        if (totalLen <= 0)
            return [(string.Empty, firstOffset)];

        string chunk = GetText(firstOffset, totalLen);

        var results = new (string Text, long StartOffset)[actualCount];
        long offset = firstOffset;
        int pos = 0;

        for (int i = 0; i < actualCount; i++)
        {
            int lfIndex = chunk.IndexOf('\n', pos);
            if (lfIndex >= 0)
            {
                results[i] = (chunk.Substring(pos, lfIndex - pos), offset);
                offset += (lfIndex - pos) + 1; // +1 for '\n'
                pos = lfIndex + 1;
            }
            else
            {
                // Last line (no trailing '\n').
                results[i] = (chunk.Substring(pos), offset);
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the length of the line at <paramref name="lineIndex"/>
    /// (excluding the terminating newline, if any).
    /// </summary>
    public long GetLineLength(long lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= LineCount)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));

        long lineStart = GetLineStartOffset(lineIndex);
        long lineEnd;

        if (lineIndex + 1 < LineCount)
            lineEnd = GetLineStartOffset(lineIndex + 1) - 1; // exclude '\n'
        else
            lineEnd = Length;

        return lineEnd - lineStart;
    }

    /// <summary>
    /// Converts a zero-based (line, column) pair to an absolute character offset.
    /// The column is clamped to the line length.
    /// </summary>
    public long LineColumnToOffset(long line, long column)
    {
        if (line < 0 || line >= LineCount)
            throw new ArgumentOutOfRangeException(nameof(line));

        long lineStart = GetLineStartOffset(line);
        long lineLen = GetLineLength(line);
        long clampedCol = Math.Min(column, lineLen);
        if (clampedCol < 0) clampedCol = 0;

        return lineStart + clampedCol;
    }

    /// <summary>
    /// Converts an absolute character offset to a zero-based (line, column) pair.
    /// Uses binary search on the line-offset cache for O(log LineCount) lookup.
    /// </summary>
    public (long Line, long Column) OffsetToLineColumn(long offset)
    {
        if (offset < 0 || offset > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (offset == 0) return (0, 0);

        EnsureLineOffsetCache();
        long[] cache = _lineOffsetCache!;

        // Binary search: find the largest line whose start offset <= offset.
        long lo = 0, hi = cache.Length - 1;
        while (lo < hi)
        {
            long mid = lo + (hi - lo + 1) / 2;
            if (cache[mid] <= offset)
                lo = mid;
            else
                hi = mid - 1;
        }

        long line = lo;
        long column = offset - cache[line];

        return (line, column);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Internal helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the absolute document offset of the first character in
    /// <paramref name="node"/>'s piece by walking up the tree.
    /// </summary>
    private long GetNodeDocumentOffset(RBNode node)
    {
        // Start with this node's left subtree length.
        long offset = node.LeftSubtreeLength;

        RBNode current = node;
        while (current.Parent != _tree.Nil && current.Parent != null)
        {
            if (current == current.Parent.Right)
            {
                // Coming from the right child means we must add
                // parent's left subtree + parent's own piece length.
                offset += current.Parent.LeftSubtreeLength + current.Parent.Piece.Length;
            }
            current = current.Parent;
        }

        return offset;
    }

    /// <summary>
    /// Reads a single character from the appropriate buffer.
    /// </summary>
    private char ReadChar(BufferType bufferType, long index)
    {
        return bufferType == BufferType.Original
            ? _original[index]
            : _addBuffer[(int)index];
    }

    /// <summary>
    /// Appends characters from a piece to a <see cref="StringBuilder"/>.
    /// </summary>
    private void AppendPieceText(StringBuilder sb, Piece piece, long offsetInPiece, long count)
    {
        long start = piece.Start + offsetInPiece;

        if (piece.BufferType == BufferType.Original)
        {
            // ITextSource.GetText returns a string.
            sb.Append(_original.GetText(start, count));
        }
        else
        {
            // Read from the add buffer.
            sb.Append(_addBuffer.ToString((int)start, (int)count));
        }
    }

    /// <summary>
    /// Counts <c>'\n'</c> characters in <paramref name="text"/>.
    /// </summary>
    private static int CountLineFeedsInString(string text)
    {
        int count = 0;
        ReadOnlySpan<char> span = text.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '\n')
                count++;
        }
        return count;
    }

    /// <summary>
    /// Walks every node in the tree and recomputes <see cref="Piece.LineFeeds"/>
    /// for any piece whose value is the sentinel <c>-1</c> (set by tree split /
    /// shrink operations that cannot compute this without buffer access).
    /// Also re-propagates augmentation so the tree is fully consistent.
    /// </summary>
    private void FixupLineFeeds()
    {
        bool anyFixed = false;

        foreach (RBNode node in _tree.InOrderNodes())
        {
            if (node.Piece.LineFeeds == -1)
            {
                int lf = CountLineFeedsForPiece(node.Piece);
                node.Piece = new Piece(
                    node.Piece.BufferType,
                    node.Piece.Start,
                    node.Piece.Length,
                    lf);
                anyFixed = true;
            }
        }

        if (anyFixed)
        {
            // Full augmentation rebuild -- guarantees consistency.
            RebuildAugmentation();
        }
    }

    /// <summary>
    /// Counts the <c>'\n'</c> characters in the buffer region described by
    /// <paramref name="piece"/>.
    /// </summary>
    private int CountLineFeedsForPiece(Piece piece)
    {
        if (piece.Length == 0) return 0;

        if (piece.BufferType == BufferType.Original)
        {
            // Fast path: use pre-computed line offsets with binary search O(log N)
            // instead of scanning the entire range O(N).
            if (_original is IPrecomputedLineFeeds precomputed
                && precomputed.LineOffsets is { } lineOffsets)
            {
                return CountLineFeedsFromOffsets(lineOffsets, piece.Start, piece.Length);
            }

            return _original.CountLineFeeds(piece.Start, piece.Length);
        }
        else
        {
            int count = 0;
            int start = (int)piece.Start;
            int end = start + (int)piece.Length;
            for (int i = start; i < end; i++)
            {
                if (_addBuffer[i] == '\n')
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Counts newlines in the original-buffer range [start, start+length) by
    /// binary-searching the pre-computed line-offset table.  Each entry in
    /// <paramref name="lineOffsets"/> is the character offset of a line start;
    /// a newline at position <c>p</c> corresponds to a line start at <c>p+1</c>.
    /// So counting entries where <c>start &lt; entry &lt;= start + length</c>
    /// gives the number of newlines in the range.
    /// </summary>
    private static int CountLineFeedsFromOffsets(long[] lineOffsets, long start, long length)
    {
        // We need entries in (start, start + length].
        long lo = start + 1;
        long hi = start + length;

        // Find first index where lineOffsets[index] >= lo.
        int left = LowerBound(lineOffsets, lo);
        // Find first index where lineOffsets[index] > hi.
        int right = UpperBound(lineOffsets, hi);

        return right - left;
    }

    /// <summary>Returns the index of the first element >= value.</summary>
    private static int LowerBound(long[] arr, long value)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (arr[mid] < value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>Returns the index of the first element > value.</summary>
    private static int UpperBound(long[] arr, long value)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (arr[mid] <= value)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Counts the number of <c>'\n'</c> characters in the document range
    /// starting at <paramref name="offset"/> with the given <paramref name="length"/>.
    /// Scans only the specified range rather than the entire document.
    /// </summary>
    public int CountLineFeedsInRange(long offset, long length)
    {
        if (length == 0) return 0;
        if (offset < 0 || length < 0 || offset + length > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int count = 0;
        long remaining = length;
        long pos = offset;

        while (remaining > 0)
        {
            var (node, offInNode) = _tree.FindByOffset(pos);
            if (node == _tree.Nil) break;

            long charsAvail = node.Piece.Length - offInNode;
            long take = Math.Min(charsAvail, remaining);
            long bufStart = node.Piece.Start + offInNode;

            if (node.Piece.BufferType == BufferType.Original)
            {
                count += _original.CountLineFeeds(bufStart, take);
            }
            else
            {
                int start = (int)bufStart;
                int end = start + (int)take;
                for (int i = start; i < end; i++)
                {
                    if (_addBuffer[i] == '\n')
                        count++;
                }
            }

            pos += take;
            remaining -= take;
        }

        return count;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Line-offset cache
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Incrementally updates the line-offset cache after an edit operation
    /// instead of invalidating and rebuilding from scratch.
    /// For a typical single-character insert with no newlines, this shifts
    /// entries after the cursor by +1 — O(lines_after) instead of O(total_chars).
    /// </summary>
    private void UpdateLineOffsetCache(long offset, long oldLength, long newLength, string? insertedText)
    {
        if (_lineOffsetCache is null) return; // Will be built lazily on first access.

        long charDelta = newLength - oldLength;

        // Count newlines added.
        int addedLines = 0;
        if (insertedText is not null)
        {
            for (int i = 0; i < insertedText.Length; i++)
            {
                if (insertedText[i] == '\n')
                    addedLines++;
            }
        }

        // Count newlines removed.
        int removedLines = 0;
        if (oldLength > 0)
        {
            // We need to count \n in the old range. The deleted text is gone from
            // the tree already, but we can compute it from the cache: count how many
            // line starts fall within [offset+1, offset+oldLength].
            long[] cache = _lineOffsetCache;
            long delEnd = offset + oldLength;
            // Binary search for first line start > offset.
            long lo = 0, hi = cache.Length - 1;
            while (lo < hi)
            {
                long mid = lo + (hi - lo) / 2;
                if (cache[mid] <= offset)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            long firstLineAfter = lo;
            // Count entries in [firstLineAfter..] that are <= delEnd.
            for (long i = firstLineAfter; i < cache.Length; i++)
            {
                if (cache[i] <= delEnd)
                    removedLines++;
                else
                    break;
            }
        }

        int lineDelta = addedLines - removedLines;

        // Find the line index containing `offset` via binary search.
        long startLine;
        {
            long[] cache = _lineOffsetCache;
            long lo2 = 0, hi2 = cache.Length - 1;
            while (lo2 < hi2)
            {
                long mid = lo2 + (hi2 - lo2 + 1) / 2;
                if (cache[mid] <= offset)
                    lo2 = mid;
                else
                    hi2 = mid - 1;
            }
            startLine = lo2;
        }

        if (lineDelta == 0)
        {
            // No lines added or removed — just shift offsets after startLine.
            if (charDelta != 0)
            {
                for (long i = startLine + 1; i < _lineOffsetCache.Length; i++)
                    _lineOffsetCache[i] += charDelta;
            }
        }
        else if (lineDelta > 0)
        {
            // Lines were added — grow the cache array.
            long oldCacheLen = _lineOffsetCache.Length;
            long newCacheLen = oldCacheLen + lineDelta;
            var newCache = new long[newCacheLen];

            // Copy entries [0..startLine] unchanged.
            for (long i = 0; i <= startLine && i < oldCacheLen; i++)
                newCache[i] = _lineOffsetCache[i];

            // Compute new line starts in the inserted text.
            long insertPos = startLine + 1;
            if (insertedText is not null)
            {
                long textOffset = offset;
                for (int i = 0; i < insertedText.Length; i++)
                {
                    if (insertedText[i] == '\n')
                    {
                        newCache[insertPos++] = textOffset + i + 1;
                    }
                }
            }

            // Skip over removed line entries and copy remaining shifted entries.
            long srcStart = startLine + 1 + removedLines;
            for (long i = srcStart; i < oldCacheLen; i++)
                newCache[insertPos++] = _lineOffsetCache[i] + charDelta;

            _lineOffsetCache = newCache;
        }
        else
        {
            // Lines were removed — shrink the cache array.
            long oldCacheLen = _lineOffsetCache.Length;
            long newCacheLen = oldCacheLen + lineDelta; // lineDelta is negative
            if (newCacheLen < 1) newCacheLen = 1;
            var newCache = new long[newCacheLen];

            // Copy entries [0..startLine] unchanged.
            for (long i = 0; i <= startLine && i < newCacheLen; i++)
                newCache[i] = _lineOffsetCache[i];

            // Compute any new line starts from inserted text.
            long insertPos = startLine + 1;
            if (insertedText is not null)
            {
                long textOffset = offset;
                for (int i = 0; i < insertedText.Length; i++)
                {
                    if (insertedText[i] == '\n')
                    {
                        if (insertPos < newCacheLen)
                            newCache[insertPos++] = textOffset + i + 1;
                    }
                }
            }

            // Skip over removed line entries and copy remaining shifted entries.
            long srcStart = startLine + 1 + removedLines;
            for (long i = srcStart; i < oldCacheLen && insertPos < newCacheLen; i++)
                newCache[insertPos++] = _lineOffsetCache[i] + charDelta;

            _lineOffsetCache = newCache;
        }

        // Safety: if the cache length doesn't match LineCount, fall back to full rebuild.
        if (_lineOffsetCache is not null && _lineOffsetCache.Length != LineCount)
            _lineOffsetCache = null;
    }

    /// <summary>
    /// Ensures the line-offset cache is built.  The cache maps every line
    /// index to its start character offset, enabling O(1) lookups and
    /// O(log N) binary-search for offset-to-line conversions.
    /// Built with a single O(N) sequential scan of the document.
    /// </summary>
    private void EnsureLineOffsetCache()
    {
        if (_lineOffsetCache is not null) return;

        long lc = LineCount;
        if (lc == 0)
        {
            _lineOffsetCache = [];
            return;
        }

        var offsets = new long[lc];
        offsets[0] = 0;
        long lineIndex = 1;
        long docOffset = 0;

        foreach (RBNode node in _tree.InOrderNodes())
        {
            if (lineIndex >= lc) break;

            long start = node.Piece.Start;
            long len = node.Piece.Length;

            for (long i = 0; i < len; i++)
            {
                if (ReadChar(node.Piece.BufferType, start + i) == '\n')
                {
                    if (lineIndex < lc)
                        offsets[lineIndex++] = docOffset + i + 1;
                }
            }

            docOffset += len;
        }

        _lineOffsetCache = offsets;
    }

    /// <summary>
    /// Rebuilds the augmented fields (<see cref="RBNode.LeftSubtreeLength"/>
    /// and <see cref="RBNode.LeftSubtreeLineFeeds"/>) for the entire tree
    /// via a post-order traversal.
    /// </summary>
    private void RebuildAugmentation()
    {
        RebuildAugmentationRecursive(_tree.Root);
    }

    private (long Length, long LineFeeds) RebuildAugmentationRecursive(RBNode node)
    {
        if (node == _tree.Nil)
            return (0, 0);

        var (leftLen, leftLF) = RebuildAugmentationRecursive(node.Left!);
        var (rightLen, rightLF) = RebuildAugmentationRecursive(node.Right!);

        node.LeftSubtreeLength = leftLen;
        node.LeftSubtreeLineFeeds = leftLF;

        long totalLen = leftLen + node.Piece.Length + rightLen;
        long totalLF = leftLF + node.Piece.LineFeeds + rightLF;

        return (totalLen, totalLF);
    }

    // ────────────────────────────────────────────────────────────────────
    //  IDisposable
    // ────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_original as IDisposable)?.Dispose();
    }
}
