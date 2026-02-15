using Bascanka.Core.Buffer;

namespace Bascanka.Editor.Controls;

/// <summary>
/// Manages the text caret (cursor) position, movement, and blink state.
/// All position calculations are delegated to the underlying <see cref="PieceTable"/>
/// to ensure consistency with the document model.
/// </summary>
public sealed class CaretManager : IDisposable
{
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

    /// <summary>Moves the caret one character to the left.</summary>
    public void MoveLeft()
    {
        if (_document is null || _offset == 0) return;
        MoveTo(_offset - 1);
    }

    /// <summary>Moves the caret one character to the right.</summary>
    public void MoveRight()
    {
        if (_document is null || _offset >= _document.Length) return;
        MoveTo(_offset + 1);
    }

    /// <summary>Moves the caret one line up, preserving the desired column.</summary>
    public void MoveUp()
    {
        if (_document is null || _line == 0) return;

        if (_desiredColumn < 0) _desiredColumn = _column;

        long newLine = _line - 1;

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

    /// <summary>Moves the caret one line down, preserving the desired column.</summary>
    public void MoveDown()
    {
        if (_document is null || _line >= _document.LineCount - 1) return;

        if (_desiredColumn < 0) _desiredColumn = _column;

        long newLine = _line + 1;

        if (Folding is not null && !Folding.IsLineVisible(newLine))
            newLine = Folding.NextVisibleLine(newLine, _document.LineCount, forward: true);

        if (newLine >= _document.LineCount) return;

        long lineLen = _document.GetLineLength(newLine);
        long col = Math.Min(_desiredColumn, lineLen);

        _line = newLine;
        _column = col;
        _offset = _document.LineColumnToOffset(newLine, col);
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

        string lineText = _document.GetLine(_line);
        long firstNonWs = 0;
        while (firstNonWs < lineText.Length && char.IsWhiteSpace(lineText[(int)firstNonWs]))
            firstNonWs++;

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
