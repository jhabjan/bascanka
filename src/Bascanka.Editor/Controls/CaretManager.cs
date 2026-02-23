using Bascanka.Core.Buffer;

namespace Bascanka.Editor.Controls;

/// <summary>
/// Manages the text caret (cursor) position, movement, and blink state.
/// All position calculations are delegated to the underlying <see cref="PieceTable"/>
/// to ensure consistency with the document model.
/// </summary>
public sealed class CaretManager : IDisposable
{
    private const long LargeLineHomeThreshold = 1_000_000;
    private const int HomePrefixScanLimit = 4096;

    private readonly System.Windows.Forms.Timer _blinkTimer;
    private PieceTable? _document;
    private long _offset;
    private long _line;
    private long _column;
    private long _desiredColumn = -1;
    private bool _visible = true;
    private bool _disposed;

    /// <summary>Raised after the caret moves to a new offset.</summary>
    public event Action<long>? CaretMoved;

    /// <summary>Raised when the caret visibility toggles (blink).</summary>
    public event Action<bool>? BlinkStateChanged;

    public CaretManager()
    {
        _blinkTimer = new System.Windows.Forms.Timer { Interval = EditorControl.DefaultCaretBlinkRate };
        _blinkTimer.Tick += OnBlinkTick;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Properties
    // ────────────────────────────────────────────────────────────────────

    /// <summary>The folding manager for skipping collapsed regions.</summary>
    public FoldingManager? Folding { get; set; }

    /// <summary>
    /// When word-wrap is active, this delegate maps (docLine, column) to a global wrap-row index.
    /// Used by <see cref="EnsureVisible"/> to scroll in wrap-row space.
    /// </summary>
    public Func<long, long, long>? LineColumnToVisibleRow { get; set; }

    /// <summary>
    /// When word-wrap is active, navigates caret up by visual wrap rows
    /// instead of document lines. Parameters: (currentLine, currentColumn, desiredColumn).
    /// Returns (newLine, newColumn), or null to fall back to default behavior.
    /// </summary>
    public Func<long, long, long, (long Line, long Column)?>? WrapMoveUp { get; set; }

    /// <summary>
    /// When word-wrap is active, navigates caret down by visual wrap rows
    /// instead of document lines. Parameters: (currentLine, currentColumn, desiredColumn).
    /// Returns (newLine, newColumn), or null to fall back to default behavior.
    /// </summary>
    public Func<long, long, long, (long Line, long Column)?>? WrapMoveDown { get; set; }

    /// <summary>
    /// Maps (docLine, charColumn) to the expanded visual column, accounting
    /// for tabs and fullwidth (CJK) characters. Used by <see cref="EnsureVisible"/>
    /// for correct horizontal scrolling.
    /// </summary>
    public Func<long, int, int>? ColumnToExpandedColumn { get; set; }

    /// <summary>The document buffer used for position calculations.</summary>
    public PieceTable? Document
    {
        get => _document;
        set
        {
            _document = value;
            _offset = 0;
            _line = 0;
            _column = 0;
            _desiredColumn = -1;
        }
    }

    /// <summary>Gets or sets the blink timer interval in milliseconds.</summary>
    public int BlinkInterval
    {
        get => _blinkTimer.Interval;
        set => _blinkTimer.Interval = Math.Max(50, value);
    }

    /// <summary>Zero-based character offset of the caret in the document.</summary>
    public long Offset => _offset;

    /// <summary>Zero-based line number of the caret.</summary>
    public long Line => _line;

    /// <summary>Zero-based column number of the caret.</summary>
    public long Column => _column;

    /// <summary>Whether the caret is currently visible (for blink rendering).</summary>
    public bool IsVisible => _visible;

    // ────────────────────────────────────────────────────────────────────
    //  Absolute positioning
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the caret to an absolute character offset.
    /// </summary>
    public void MoveTo(long offset)
    {
        if (_document is null) return;

        _offset = Math.Clamp(offset, 0, _document.Length);
        SyncLineColumnFromOffset();
        _desiredColumn = _column;
        ResetBlink();
        CaretMoved?.Invoke(_offset);
    }

    /// <summary>
    /// Moves the caret to a specific line and column.
    /// </summary>
    public void MoveToLineColumn(long line, long column)
    {
        if (_document is null) return;

        line = Math.Clamp(line, 0, _document.LineCount - 1);
        long lineLen = _document.GetLineLength(line);
        column = Math.Clamp(column, 0, lineLen);

        _line = line;
        _column = column;
        _offset = _document.LineColumnToOffset(line, column);
        _desiredColumn = _column;
        ResetBlink();
        CaretMoved?.Invoke(_offset);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Relative movement
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Moves the caret one character to the left, skipping over surrogate pairs.</summary>
    public void MoveLeft()
    {
        if (_document is null || _offset == 0) return;
        long newOffset = _offset;

        do
        {
            newOffset--;
            if (newOffset <= 0) { newOffset = 0; break; }

            // Skip BMP zero-width characters (ZWJ, variation selectors) going backwards.
            while (newOffset > 0 && IsZeroWidthChar(_document.GetCharAt(newOffset)))
                newOffset--;

            // If we landed on a low surrogate, skip back to the high surrogate.
            if (newOffset > 0 && char.IsLowSurrogate(_document.GetCharAt(newOffset)))
                newOffset--;
        }
        while (newOffset > 0 && (IsSkinToneModifierAt(newOffset) || IsSecondRegionalIndicatorAt(newOffset)));

        MoveTo(newOffset);
    }

    /// <summary>Moves the caret one character to the right, skipping over surrogate pairs.</summary>
    public void MoveRight()
    {
        if (_document is null || _offset >= _document.Length) return;
        long newOffset = _offset + 1;
        // If we were on a high surrogate, skip the low surrogate too.
        if (char.IsHighSurrogate(_document.GetCharAt(_offset)) && newOffset < _document.Length)
            newOffset++;
        // Skip over any following zero-width characters (BMP and supplementary).
        while (newOffset < _document.Length)
        {
            if (IsZeroWidthChar(_document.GetCharAt(newOffset)))
            {
                newOffset++;
                continue;
            }
            if (IsSkinToneModifierAt(newOffset))
            {
                newOffset += 2;
                continue;
            }
            if (IsSecondRegionalIndicatorAt(newOffset))
            {
                newOffset += 2;
                continue;
            }
            break;
        }
        MoveTo(newOffset);
    }

    private static bool IsZeroWidthChar(char c) =>
        c == '\u200B' || c == '\u200C' || c == '\u200D' ||
        c == '\uFE0E' || c == '\uFE0F' || c == '\u2060' || c == '\uFEFF' ||
        c == '\u20E3' ||
        (c >= '\u0300' && c <= '\u036F') ||  // Combining Diacritical Marks
        (c >= '\u0483' && c <= '\u0489') ||  // Combining Cyrillic
        (c >= '\u1AB0' && c <= '\u1AFF') ||  // Combining Diacritical Marks Extended
        (c >= '\u1DC0' && c <= '\u1DFF') ||  // Combining Diacritical Marks Supplement
        (c >= '\u20D0' && c <= '\u20FF') ||  // Combining Diacritical Marks for Symbols
        (c >= '\uFE20' && c <= '\uFE2F');    // Combining Half Marks

    /// <summary>
    /// Returns true if the surrogate pair at <paramref name="offset"/> is a
    /// skin tone modifier (U+1F3FB–U+1F3FF), which should be treated as zero-width.
    /// </summary>
    private bool IsSkinToneModifierAt(long offset)
    {
        if (_document is null || offset + 1 >= _document.Length) return false;
        char hi = _document.GetCharAt(offset);
        if (!char.IsHighSurrogate(hi)) return false;
        char lo = _document.GetCharAt(offset + 1);
        if (!char.IsLowSurrogate(lo)) return false;
        int cp = char.ConvertToUtf32(hi, lo);
        return cp >= 0x1F3FB && cp <= 0x1F3FF;
    }

    /// <summary>
    /// Returns true if the surrogate pair at <paramref name="offset"/> is the
    /// second Regional Indicator in a flag pair (e.g. 🇧 in 🇬🇧).
    /// Counts consecutive preceding Regional Indicators to determine pairing.
    /// </summary>
    private bool IsSecondRegionalIndicatorAt(long offset)
    {
        if (_document is null || offset + 1 >= _document.Length || offset < 2) return false;
        char hi = _document.GetCharAt(offset);
        if (!char.IsHighSurrogate(hi)) return false;
        char lo = _document.GetCharAt(offset + 1);
        if (!char.IsLowSurrogate(lo)) return false;
        int cp = char.ConvertToUtf32(hi, lo);
        if (cp < 0x1F1E0 || cp > 0x1F1FF) return false;

        // Count consecutive preceding Regional Indicators.
        int riCount = 0;
        long j = offset;
        while (j >= 2)
        {
            j -= 2;
            char prevHi = _document.GetCharAt(j);
            char prevLo = _document.GetCharAt(j + 1);
            if (!char.IsHighSurrogate(prevHi) || !char.IsLowSurrogate(prevLo)) break;
            int prevCp = char.ConvertToUtf32(prevHi, prevLo);
            if (prevCp < 0x1F1E0 || prevCp > 0x1F1FF) break;
            riCount++;
        }
        return riCount % 2 == 1;
    }

    /// <summary>Moves the caret one line up, preserving the desired column.</summary>
    public void MoveUp()
    {
        if (_document is null) return;

        if (_desiredColumn < 0) _desiredColumn = _column;

        // Word-wrap: navigate by visual wrap rows.
        if (WrapMoveUp is not null)
        {
            var result = WrapMoveUp(_line, _column, _desiredColumn);
            if (result is not null)
            {
                var (newLine, newCol) = result.Value;
                _line = newLine;
                _column = newCol;
                _offset = _document.LineColumnToOffset(newLine, newCol);
                ResetBlink();
                CaretMoved?.Invoke(_offset);
                return;
            }
        }

        if (_line == 0) return;

        long targetLine = _line - 1;

        if (Folding is not null && !Folding.IsLineVisible(targetLine))
            targetLine = Folding.NextVisibleLine(targetLine, _document.LineCount, forward: false);

        long lineLen = _document.GetLineLength(targetLine);
        long col = Math.Min(_desiredColumn, lineLen);

        _line = targetLine;
        _column = col;
        _offset = _document.LineColumnToOffset(targetLine, col);
        ResetBlink();
        CaretMoved?.Invoke(_offset);
    }

    /// <summary>Moves the caret one line down, preserving the desired column.</summary>
    public void MoveDown()
    {
        if (_document is null) return;

        if (_desiredColumn < 0) _desiredColumn = _column;

        // Word-wrap: navigate by visual wrap rows.
        if (WrapMoveDown is not null)
        {
            var result = WrapMoveDown(_line, _column, _desiredColumn);
            if (result is not null)
            {
                var (newLine, newCol) = result.Value;
                _line = newLine;
                _column = newCol;
                _offset = _document.LineColumnToOffset(newLine, newCol);
                ResetBlink();
                CaretMoved?.Invoke(_offset);
                return;
            }
        }

        if (_line >= _document.LineCount - 1) return;

        long targetLine = _line + 1;

        if (Folding is not null && !Folding.IsLineVisible(targetLine))
            targetLine = Folding.NextVisibleLine(targetLine, _document.LineCount, forward: true);

        if (targetLine >= _document.LineCount) return;

        long lineLen = _document.GetLineLength(targetLine);
        long col = Math.Min(_desiredColumn, lineLen);

        _line = targetLine;
        _column = col;
        _offset = _document.LineColumnToOffset(targetLine, col);
        ResetBlink();
        CaretMoved?.Invoke(_offset);
    }

    /// <summary>Moves the caret one word to the left.</summary>
    public void MoveWordLeft()
    {
        if (_document is null || _offset == 0) return;

        long pos = _offset - 1;

        // Skip whitespace
        while (pos > 0 && char.IsWhiteSpace(_document.GetCharAt(pos)))
            pos--;

        // Skip word characters
        if (pos > 0 && IsWordChar(_document.GetCharAt(pos)))
        {
            while (pos > 0 && IsWordChar(_document.GetCharAt(pos - 1)))
                pos--;
        }
        else if (pos > 0)
        {
            // Skip non-word, non-whitespace (punctuation)
            while (pos > 0 && !IsWordChar(_document.GetCharAt(pos - 1)) &&
                   !char.IsWhiteSpace(_document.GetCharAt(pos - 1)))
                pos--;
        }

        MoveTo(pos);
    }

    /// <summary>Moves the caret one word to the right.</summary>
    public void MoveWordRight()
    {
        if (_document is null || _offset >= _document.Length) return;

        long pos = _offset;
        long len = _document.Length;

        // Skip current word characters
        if (pos < len && IsWordChar(_document.GetCharAt(pos)))
        {
            while (pos < len && IsWordChar(_document.GetCharAt(pos)))
                pos++;
        }
        else if (pos < len && !char.IsWhiteSpace(_document.GetCharAt(pos)))
        {
            // Skip punctuation
            while (pos < len && !IsWordChar(_document.GetCharAt(pos)) &&
                   !char.IsWhiteSpace(_document.GetCharAt(pos)))
                pos++;
        }

        // Skip whitespace
        while (pos < len && char.IsWhiteSpace(_document.GetCharAt(pos)))
            pos++;

        MoveTo(pos);
    }

    /// <summary>
    /// Moves the caret to the beginning of the line. On first press, moves to
    /// the first non-whitespace character; on second press, moves to column 0.
    /// </summary>
    public void MoveHome()
    {
        if (_document is null) return;

        long lineLen = _document.GetLineLength(_line);
        long firstNonWs = 0;

        if (lineLen > LargeLineHomeThreshold)
        {
            // Avoid materializing very large lines on Home/Shift+Home.
            long lineStart = _document.GetLineStartOffset(_line);
            long scanLimit = Math.Min(lineLen, HomePrefixScanLimit);
            while (firstNonWs < scanLimit && char.IsWhiteSpace(_document.GetCharAt(lineStart + firstNonWs)))
                firstNonWs++;

            // If indentation extends beyond the scan window, prefer column 0.
            if (firstNonWs >= scanLimit && scanLimit < lineLen)
                firstNonWs = 0;
        }
        else
        {
            string lineText = _document.GetLine(_line);
            while (firstNonWs < lineText.Length && char.IsWhiteSpace(lineText[(int)firstNonWs]))
                firstNonWs++;
        }

        if (_column == firstNonWs && firstNonWs != 0)
        {
            // Already at first non-whitespace; go to column 0.
            MoveToLineColumn(_line, 0);
        }
        else
        {
            // Go to first non-whitespace.
            MoveToLineColumn(_line, firstNonWs);
        }
    }

    /// <summary>Moves the caret to the end of the current line.</summary>
    public void MoveEnd()
    {
        if (_document is null) return;

        long lineLen = _document.GetLineLength(_line);
        MoveToLineColumn(_line, lineLen);
    }

    /// <summary>Moves the caret one page up.</summary>
    public void MovePageUp(int visibleLines)
    {
        if (_document is null) return;

        if (_desiredColumn < 0) _desiredColumn = _column;

        long newLine = Math.Max(0, _line - visibleLines);

        if (Folding is not null && !Folding.IsLineVisible(newLine))
            newLine = Folding.NextVisibleLine(newLine, _document.LineCount, forward: false);

        long lineLen = _document.GetLineLength(newLine);
        long col = Math.Min(_desiredColumn, lineLen);

        _line = newLine;
        _column = col;
        _offset = _document.LineColumnToOffset(newLine, col);
        ResetBlink();
        CaretMoved?.Invoke(_offset);
    }

    /// <summary>Moves the caret one page down.</summary>
    public void MovePageDown(int visibleLines)
    {
        if (_document is null) return;

        if (_desiredColumn < 0) _desiredColumn = _column;

        long newLine = Math.Min(_document.LineCount - 1, _line + visibleLines);

        if (Folding is not null && !Folding.IsLineVisible(newLine))
            newLine = Folding.NextVisibleLine(newLine, _document.LineCount, forward: true);

        if (newLine >= _document.LineCount) newLine = _document.LineCount - 1;

        long lineLen = _document.GetLineLength(newLine);
        long col = Math.Min(_desiredColumn, lineLen);

        _line = newLine;
        _column = col;
        _offset = _document.LineColumnToOffset(newLine, col);
        ResetBlink();
        CaretMoved?.Invoke(_offset);
    }

    /// <summary>Moves the caret to the start of the document (Ctrl+Home).</summary>
    public void MoveToDocumentStart()
    {
        MoveTo(0);
    }

    /// <summary>Moves the caret to the end of the document (Ctrl+End).</summary>
    public void MoveToDocumentEnd()
    {
        if (_document is null) return;
        MoveTo(_document.Length);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Scroll integration
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adjusts the scroll manager so that the caret is visible.
    /// </summary>
    public void EnsureVisible(ScrollManager scroll, int visibleLines, int visibleColumns)
    {
        if (scroll is null) return;

        // Convert document line to visible-line index.
        long visLine;
        if (LineColumnToVisibleRow is not null)
        {
            // Word-wrap active: map (line, column) to a global wrap-row index.
            visLine = LineColumnToVisibleRow(_line, _column);
        }
        else if (Folding is not null)
        {
            visLine = Folding.DocumentLineToVisibleLine(_line);
        }
        else
        {
            visLine = _line;
        }
        if (visLine < 0) visLine = _line; // fallback if line is hidden

        // Vertical
        if (visLine < scroll.FirstVisibleLine)
        {
            scroll.ScrollToLine(visLine);
        }
        else if (visLine >= scroll.FirstVisibleLine + visibleLines)
        {
            scroll.ScrollToLine(visLine - visibleLines + 1);
        }

        // Horizontal (no horizontal scroll when word-wrap is on)
        if (LineColumnToVisibleRow is null)
        {
            // Use expanded column (accounts for tabs and fullwidth chars)
            // so horizontal scroll aligns with the actual pixel position.
            long expandedCol = ColumnToExpandedColumn is not null
                ? ColumnToExpandedColumn(_line, (int)_column)
                : _column;

            if (expandedCol < scroll.HorizontalScrollOffset)
            {
                scroll.HorizontalScrollOffset = (int)Math.Max(0, expandedCol - EditorControl.DefaultCaretScrollBuffer);
            }
            else if (expandedCol >= scroll.HorizontalScrollOffset + visibleColumns)
            {
                scroll.HorizontalScrollOffset = (int)(expandedCol - visibleColumns + EditorControl.DefaultCaretScrollBuffer);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Blink timer
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Starts the blink timer.</summary>
    public void StartBlink()
    {
        _visible = true;
        _blinkTimer.Start();
    }

    /// <summary>Stops the blink timer and forces the caret visible.</summary>
    public void StopBlink()
    {
        _blinkTimer.Stop();
        _visible = true;
        BlinkStateChanged?.Invoke(_visible);
    }

    /// <summary>Resets the blink cycle so the caret becomes visible immediately.</summary>
    public void ResetBlink()
    {
        _blinkTimer.Stop();
        _visible = true;
        BlinkStateChanged?.Invoke(_visible);
        _blinkTimer.Start();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Internals
    // ────────────────────────────────────────────────────────────────────

    private void SyncLineColumnFromOffset()
    {
        if (_document is null || _document.Length == 0)
        {
            _line = 0;
            _column = 0;
            return;
        }

        long clampedOffset = Math.Min(_offset, _document.Length);
        var (line, column) = _document.OffsetToLineColumn(clampedOffset);
        _line = line;
        _column = column;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        _visible = !_visible;
        BlinkStateChanged?.Invoke(_visible);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _blinkTimer.Stop();
        _blinkTimer.Dispose();
    }
}
