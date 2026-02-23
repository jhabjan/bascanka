using Bascanka.Core.Buffer;

namespace Bascanka.Editor.Controls;

/// <summary>
/// Manages text selection state including start/end anchors and provides
/// methods for programmatic selection (word, line, all).
/// </summary>
public sealed class SelectionManager
{
    private PieceTable? _document;
    private long _anchorOffset = -1;
    private long _selectionStart = -1;
    private long _selectionEnd = -1;

    // Column (box) selection state.
    private bool _isColumnMode;
    private long _colAnchorLine, _colAnchorCol;
    private long _colActiveLine, _colActiveCol;

    /// <summary>Raised when the selection changes.</summary>
    public event Action? SelectionChanged;

    // ────────────────────────────────────────────────────────────────────
    //  Properties
    // ────────────────────────────────────────────────────────────────────

    /// <summary>The document buffer used for text retrieval.</summary>
    public PieceTable? Document
    {
        get => _document;
        set
        {
            _document = value;
            ClearSelection();
        }
    }

    /// <summary>
    /// Start of the selection (the lower offset). Returns -1 if no selection.
    /// </summary>
    public long SelectionStart => _selectionStart;

    /// <summary>
    /// End of the selection (the higher offset, exclusive). Returns -1 if no selection.
    /// </summary>
    public long SelectionEnd => _selectionEnd;

    /// <summary>
    /// The anchor offset where the selection was initiated.
    /// Used internally for extending the selection in the correct direction.
    /// </summary>
    public long AnchorOffset => _anchorOffset;

    /// <summary>
    /// <see langword="true"/> if a non-empty selection is active.
    /// </summary>
    public bool HasSelection =>
        _selectionStart >= 0 && _selectionEnd >= 0 && _selectionStart != _selectionEnd;

    // ── Column selection properties ─────────────────────────────────────

    /// <summary>Whether the selection is in column (box) mode.</summary>
    public bool IsColumnMode => _isColumnMode;

    /// <summary>The anchor line where Alt+click started.</summary>
    public long ColumnAnchorLine => _colAnchorLine;

    /// <summary>The anchor column where Alt+click started.</summary>
    public long ColumnAnchorCol => _colAnchorCol;

    /// <summary>The active line (where mouse currently is).</summary>
    public long ColumnActiveLine => _colActiveLine;

    /// <summary>The active column (where mouse currently is).</summary>
    public long ColumnActiveCol => _colActiveCol;

    /// <summary>First line of the column selection rectangle.</summary>
    public long ColumnStartLine => Math.Min(_colAnchorLine, _colActiveLine);

    /// <summary>Last line of the column selection rectangle (inclusive).</summary>
    public long ColumnEndLine => Math.Max(_colAnchorLine, _colActiveLine);

    /// <summary>Left column of the column selection rectangle.</summary>
    public long ColumnLeftCol => Math.Min(_colAnchorCol, _colActiveCol);

    /// <summary>Right column of the column selection rectangle (exclusive).</summary>
    public long ColumnRightCol => Math.Max(_colAnchorCol, _colActiveCol);

    /// <summary>
    /// <see langword="true"/> if a non-empty column selection is active.
    /// </summary>
    public bool HasColumnSelection =>
        _isColumnMode &&
        (ColumnStartLine != ColumnEndLine || ColumnLeftCol != ColumnRightCol);

    // ────────────────────────────────────────────────────────────────────
    //  Selection operations
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the selected text, or an empty string if there is no selection.
    /// </summary>
    public string GetSelectedText()
    {
        if (!HasSelection || _document is null)
            return string.Empty;

        long len = _selectionEnd - _selectionStart;
        return _document.GetText(_selectionStart, len);
    }

    /// <summary>
    /// Begins a new selection at the specified character offset.
    /// </summary>
    public void StartSelection(long offset)
    {
        if (!_isColumnMode &&
            _anchorOffset == offset &&
            _selectionStart == offset &&
            _selectionEnd == offset)
            return;

        _anchorOffset = offset;
        _selectionStart = offset;
        _selectionEnd = offset;
        _isColumnMode = false;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Extends the selection from the anchor to the specified offset.
    /// </summary>
    public void ExtendSelection(long offset)
    {
        if (_anchorOffset < 0)
        {
            StartSelection(offset);
            return;
        }

        long newStart;
        long newEnd;
        if (offset < _anchorOffset)
        {
            newStart = offset;
            newEnd = _anchorOffset;
        }
        else
        {
            newStart = _anchorOffset;
            newEnd = offset;
        }

        if (!_isColumnMode && _selectionStart == newStart && _selectionEnd == newEnd)
            return;

        _isColumnMode = false;
        _selectionStart = newStart;
        _selectionEnd = newEnd;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Selects the entire document.
    /// </summary>
    public void SelectAll()
    {
        if (_document is null) return;

        _anchorOffset = 0;
        _selectionStart = 0;
        _selectionEnd = _document.Length;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Selects the word at the given character offset (used for double-click).
    /// A "word" is defined as a contiguous run of letters, digits, or underscores.
    /// </summary>
    public void SelectWord(long offset)
    {
        if (_document is null || _document.Length == 0) return;

        offset = Math.Clamp(offset, 0, _document.Length - 1);

        char ch = _document.GetCharAt(offset);
        long start = offset;
        long end = offset + 1;
        long docLen = _document.Length;

        if (IsWordChar(ch))
        {
            while (start > 0 && IsWordChar(_document.GetCharAt(start - 1)))
                start--;
            while (end < docLen && IsWordChar(_document.GetCharAt(end)))
                end++;
        }
        else if (char.IsWhiteSpace(ch))
        {
            while (start > 0 && char.IsWhiteSpace(_document.GetCharAt(start - 1)))
                start--;
            while (end < docLen && char.IsWhiteSpace(_document.GetCharAt(end)))
                end++;
        }
        else
        {
            // Punctuation: select contiguous punctuation
            while (start > 0 && !IsWordChar(_document.GetCharAt(start - 1)) &&
                   !char.IsWhiteSpace(_document.GetCharAt(start - 1)))
                start--;
            while (end < docLen && !IsWordChar(_document.GetCharAt(end)) &&
                   !char.IsWhiteSpace(_document.GetCharAt(end)))
                end++;
        }

        _anchorOffset = start;
        _selectionStart = start;
        _selectionEnd = end;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Selects the entire line at the given zero-based line index
    /// (used for triple-click or gutter click). The selection includes
    /// the line terminator if present.
    /// </summary>
    public void SelectLine(long line)
    {
        if (_document is null) return;

        line = Math.Clamp(line, 0, _document.LineCount - 1);

        long start = _document.GetLineStartOffset(line);
        long end;

        if (line + 1 < _document.LineCount)
        {
            end = _document.GetLineStartOffset(line + 1);
        }
        else
        {
            end = _document.Length;
        }

        _anchorOffset = start;
        _selectionStart = start;
        _selectionEnd = end;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Clears the current selection (both stream and column).
    /// </summary>
    public void ClearSelection()
    {
        bool hadSelection = HasSelection || HasColumnSelection;
        _anchorOffset = -1;
        _selectionStart = -1;
        _selectionEnd = -1;
        _isColumnMode = false;

        if (hadSelection)
            SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Returns whether the given offset falls within the current selection.
    /// </summary>
    public bool IsOffsetSelected(long offset)
    {
        return HasSelection && offset >= _selectionStart && offset < _selectionEnd;
    }

    /// <summary>
    /// Returns whether the given line has any selected characters.
    /// </summary>
    public bool IsLinePartiallySelected(long lineIndex, PieceTable document)
    {
        if (!HasSelection) return false;

        long lineStart = document.GetLineStartOffset(lineIndex);
        long lineEnd;
        if (lineIndex + 1 < document.LineCount)
            lineEnd = document.GetLineStartOffset(lineIndex + 1);
        else
            lineEnd = document.Length;

        return _selectionStart < lineEnd && _selectionEnd > lineStart;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Column (box) selection operations
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Begins a column selection at the specified line and column.
    /// </summary>
    public void StartColumnSelection(long line, long column)
    {
        if (_isColumnMode &&
            _colAnchorLine == line && _colAnchorCol == column &&
            _colActiveLine == line && _colActiveCol == column)
            return;

        _isColumnMode = true;
        _colAnchorLine = line;
        _colAnchorCol = column;
        _colActiveLine = line;
        _colActiveCol = column;
        // Clear stream selection.
        _anchorOffset = -1;
        _selectionStart = -1;
        _selectionEnd = -1;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Extends the column selection to the specified line and column.
    /// </summary>
    public void ExtendColumnSelection(long line, long column)
    {
        if (!_isColumnMode) return;
        if (_colActiveLine == line && _colActiveCol == column) return;
        _colActiveLine = line;
        _colActiveCol = column;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Gets the selected text for each line in the column rectangle.
    /// Column bounds are in expanded (visual) columns; they are converted
    /// to character indices per line.
    /// </summary>
    public List<string> GetColumnSelectedLines(PieceTable document, int tabSize)
    {
        var result = new List<string>();
        if (!HasColumnSelection) return result;

        long startLine = ColumnStartLine;
        long endLine = ColumnEndLine;
        int leftExpCol = (int)ColumnLeftCol;
        int rightExpCol = (int)ColumnRightCol;

        for (long line = startLine; line <= endLine; line++)
        {
            if (line >= document.LineCount) break;
            string lineText = document.GetLine(line);
            // Strip trailing '\r' so column calculations are based on visible characters only.
            if (lineText.Length > 0 && lineText[^1] == '\r')
                lineText = lineText[..^1];
            int charLeft = CompressedColumnAt(lineText, leftExpCol, tabSize);
            int charRight = CompressedColumnAt(lineText, rightExpCol, tabSize);
            if (charRight > charLeft)
                result.Add(lineText.Substring(charLeft, charRight - charLeft));
            else
                result.Add(string.Empty);
        }

        return result;
    }

    /// <summary>
    /// Gets column-selected text as a single string (lines joined by newline).
    /// </summary>
    public string GetColumnSelectedText(PieceTable document, int tabSize)
    {
        return string.Join("\n", GetColumnSelectedLines(document, tabSize));
    }

    // ────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Converts an expanded (visual) column to a character index in the
    /// line text, accounting for tab stops. If the expanded column is past
    /// the end of the line, returns the line length.
    /// </summary>
    public static int CompressedColumnAt(string lineText, int expandedCol, int tabSize)
    {
        int col = 0;
        for (int i = 0; i < lineText.Length; i++)
        {
            if (char.IsLowSurrogate(lineText[i])) continue; // skip low surrogates
            if (col >= expandedCol) return i;
            if (lineText[i] == '\t')
                col += tabSize - (col % tabSize);
            else
                col += EditorSurface.GetCharDisplayWidth(lineText, i);
        }
        return lineText.Length;
    }
}
