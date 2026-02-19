using System.Drawing;
using Bascanka.Core.Buffer;
using Bascanka.Core.Diff;
using Bascanka.Core.Search;
using Bascanka.Core.Syntax;
using Bascanka.Editor.Highlighting;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Controls;

/// <summary>
/// The main rendering surface for the text editor.  Inherits from
/// <see cref="Control"/> with double-buffering enabled and paints only
/// the visible lines using GDI (<see cref="TextRenderer"/>) for
/// pixel-sharp text rendering.
/// </summary>
public sealed class EditorSurface : Control
{
    private static readonly TextFormatFlags DrawFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
        TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.SingleLine;

    /// <summary>Horizontal padding in pixels between the gutter and text content.</summary>
    private static int TextLeftPadding => EditorControl.DefaultTextLeftPadding;

    private PieceTable? _document;
    private Font _editorFont;
    private ITheme _theme;
    private int _tabSize = EditorControl.DefaultTabWidth;
    private bool _wordWrap;
    private bool _showWhitespace;

    // Calculated font metrics.
    private int _charWidth;
    private int _cjkCharWidth;
    private int _suppCjkCharWidth; // supplementary plane CJK (Extension B+)
    private int _bmpEmojiCharWidth;  // BMP emoji (e.g. ✅) via Segoe UI Symbol
    private int _suppEmojiCharWidth; // supplementary emoji (e.g. 😀) via Segoe UI Symbol/Emoji
    private Font? _emojiFallbackFont; // Segoe UI Emoji for keycap sequences (GDI can't combine them with monospace fonts)
    private int _lineHeight;

    // References to collaborating managers.
    private CaretManager? _caret;
    private SelectionManager? _selection;
    private ScrollManager? _scroll;
    private FoldingManager? _folding;
    private TokenCache? _tokenCache;
    private ILexer? _lexer;

    // Mouse state for click and drag selection.
    private bool _mouseDown;
    private int _clickCount;
    private DateTime _lastClickTime;
    private Point _lastClickPoint;

    // ────────────────────────────────────────────────────────────────────
    //  Constructor
    // ────────────────────────────────────────────────────────────────────

    public EditorSurface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        _editorFont = new Font(EditorControl.DefaultFontFamily, EditorControl.DefaultFontSize, FontStyle.Regular, GraphicsUnit.Point);
        _theme = new DarkTheme();

        BackColor = _theme.EditorBackground;
        Cursor = Cursors.IBeam;

        RecalcFontMetrics();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Properties
    // ────────────────────────────────────────────────────────────────────

    /// <summary>The document buffer to render.</summary>
    public PieceTable? Document
    {
        get => _document;
        set
        {
            _document = value;
            Invalidate();
        }
    }

    /// <summary>The monospace font used for rendering text.</summary>
    public new Font Font
    {
        get => _editorFont;
        set
        {
            _editorFont = value ?? throw new ArgumentNullException(nameof(value));
            RecalcFontMetrics();
            Invalidate();
        }
    }

    /// <summary>Number of spaces per tab stop.</summary>
    public int TabSize
    {
        get => _tabSize;
        set
        {
            _tabSize = Math.Max(1, value);
            Invalidate();
        }
    }

    /// <summary>The active colour theme.</summary>
    public ITheme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? throw new ArgumentNullException(nameof(value));
            BackColor = _theme.EditorBackground;
            Invalidate();
        }
    }

    /// <summary>Whether word wrap is enabled.</summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            _wordWrap = value;
            Invalidate();
        }
    }

    /// <summary>Whether whitespace characters (spaces, tabs, line endings) are rendered.</summary>
    public bool ShowWhitespace
    {
        get => _showWhitespace;
        set
        {
            _showWhitespace = value;
            Invalidate();
        }
    }

    /// <summary>
    /// The maximum number of expanded columns that fit in the viewport.
    /// Used by word wrap to determine where to break lines.
    /// </summary>
    internal int WrapColumns => _charWidth > 0 ? Math.Max(20, (ClientSize.Width - TextLeftPadding) / _charWidth) : 80;

    /// <summary>
    /// Returns the number of visual rows a document line occupies when word wrap is enabled.
    /// </summary>
    internal int GetWrapRowCount(long docLine)
    {
        if (!_wordWrap || _document is null || docLine >= _document.LineCount)
            return 1;
        int wrapCols = WrapColumns;

        // Fast path: use character count directly for long lines.
        // Avoids fetching the full line text and calling ExpandTabs.
        // Exact for tab-free text (JSON, most long lines); may undercount
        // slightly for lines with many tabs, which is acceptable.
        long len = _document.GetLineLength(docLine);
        if (len > wrapCols * 4)
            return Math.Max(1, (int)((len + wrapCols - 1) / wrapCols));

        string lineText = _document.GetLine(docLine);
        string expanded = ExpandTabs(lineText);
        return Math.Max(1, (expanded.Length + wrapCols - 1) / wrapCols);
    }

    /// <summary>Width of a single character cell in pixels.</summary>
    public int CharWidth => _charWidth;

    /// <summary>Height of a single line in pixels.</summary>
    public int LineHeight => _lineHeight;

    /// <summary>Number of fully visible lines in the viewport.</summary>
    public int VisibleLineCount => _lineHeight > 0 ? ClientSize.Height / _lineHeight : 1;

    /// <summary>Number of fully visible columns in the viewport.</summary>
    public int MaxVisibleColumns => _charWidth > 0 ? ClientSize.Width / _charWidth : 1;

    // ── Manager references ─────────────────────────────────────────────

    public CaretManager? Caret { get => _caret; set => _caret = value; }
    public SelectionManager? Selection { get => _selection; set => _selection = value; }
    public ScrollManager? Scroll { get => _scroll; set => _scroll = value; }
    public FoldingManager? Folding { get => _folding; set => _folding = value; }
    public TokenCache? Tokens { get => _tokenCache; set => _tokenCache = value; }
    public ILexer? Lexer { get => _lexer; set => _lexer = value; }

    /// <summary>The <see cref="InputHandler"/> that processes keyboard input.</summary>
    public InputHandler? InputHandler { get; set; }

    /// <summary>
    /// Compiled regex for highlighting search matches on visible lines.
    /// Set by the find panel; <see langword="null"/> when no search is active.
    /// </summary>
    public System.Text.RegularExpressions.Regex? SearchHighlightPattern { get; set; }

    /// <summary>
    /// Per-line diff metadata for diff view mode. When set, diff background
    /// colours are rendered behind text lines.
    /// </summary>
    public DiffLine[]? DiffLineMarkers { get; set; }

    /// <summary>
    /// Custom highlighting matcher for user-defined regex-based coloring.
    /// When set, custom highlighting is used instead of syntax tokens.
    /// </summary>
    public CustomHighlightMatcher? CustomHighlightMatcher { get; set; }

    /// <summary>
    /// Pre-computed block regions for custom highlighting.
    /// When set, block background/foreground colors are rendered behind text.
    /// </summary>
    public IReadOnlyList<BlockRegion>? CustomBlockRegions { get; set; }

    // ────────────────────────────────────────────────────────────────────
    //  Word-wrap row mapping
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the total number of visual rows across all visible document lines
    /// when word-wrap is enabled. Falls back to document/visible line count otherwise.
    /// </summary>
    internal long GetTotalWrapRows()
    {
        if (!_wordWrap || _document is null)
        {
            long lineCount = _document?.LineCount ?? 0;
            return _folding is not null ? _folding.GetVisibleLineCount(lineCount) : lineCount;
        }

        long total = 0;
        long docLines = _document.LineCount;
        int wrapCols = WrapColumns;

        for (long line = 0; line < docLines; line++)
        {
            if (_folding is not null && !_folding.IsLineVisible(line))
                continue;

            // Fast path: use line length to check if wrapping is needed.
            // GetLineLength is O(1) with cached offsets; avoids string allocation.
            long len = _document.GetLineLength(line);
            if (len <= wrapCols)
            {
                total += 1;
            }
            else
            {
                total += GetWrapRowCount(line);
            }
        }

        return Math.Max(1, total);
    }

    /// <summary>
    /// Maps a global wrap-row index to the document line and offset within that line's wrap rows.
    /// </summary>
    internal (long DocLine, int WrapRowOffset) WrapRowToDocumentLine(long wrapRow)
    {
        if (_document is null) return (0, 0);

        long docLines = _document.LineCount;
        int wrapCols = WrapColumns;
        long accumulated = 0;

        for (long line = 0; line < docLines; line++)
        {
            if (_folding is not null && !_folding.IsLineVisible(line))
                continue;

            long len = _document.GetLineLength(line);
            int rows = len <= wrapCols ? 1 : GetWrapRowCount(line);

            if (accumulated + rows > wrapRow)
                return (line, (int)(wrapRow - accumulated));

            accumulated += rows;
        }

        return (Math.Max(0, docLines - 1), 0);
    }

    /// <summary>
    /// Maps a document line and wrap-row offset within that line to a global wrap-row index.
    /// </summary>
    internal long DocumentLineToWrapRow(long docLine, int wrapRowOffset = 0)
    {
        if (_document is null) return 0;

        long docLines = _document.LineCount;
        int wrapCols = WrapColumns;
        long accumulated = 0;

        for (long line = 0; line < docLines; line++)
        {
            if (_folding is not null && !_folding.IsLineVisible(line))
                continue;

            if (line == docLine)
                return accumulated + wrapRowOffset;

            long len = _document.GetLineLength(line);
            accumulated += len <= wrapCols ? 1 : GetWrapRowCount(line);
        }

        return accumulated;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Font metrics
    // ────────────────────────────────────────────────────────────────────

    private void RecalcFontMetrics()
    {
        using var g = CreateGraphics();
        Size size = TextRenderer.MeasureText(g, "M", _editorFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        _charWidth = Math.Max(1, size.Width);
        _lineHeight = Math.Max(1, size.Height + EditorControl.DefaultLineSpacing);

        // Measure actual CJK character width via font fallback.
        Size cjkSize = TextRenderer.MeasureText(g, "\u4E2D", _editorFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        _cjkCharWidth = Math.Max(_charWidth, cjkSize.Width);

        // Measure supplementary-plane CJK (Extension B) — may use a different
        // fallback font (e.g. SimSun-ExtB) with different metrics.
        Size suppCjkSize = TextRenderer.MeasureText(g, "\U00020BB7", _editorFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        _suppCjkCharWidth = Math.Max(_charWidth, suppCjkSize.Width);

        // Measure emoji advance widths using differential method to eliminate
        // per-string overhead from MeasureText.  BMP and supplementary emoji
        // may use different fallback fonts with different metrics.
        var mflags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        // BMP emoji (U+2705 ✅)
        Size bmpE1 = TextRenderer.MeasureText(g, "\u2705", _editorFont, Size.Empty, mflags);
        Size bmpE2 = TextRenderer.MeasureText(g, "\u2705\u2705", _editorFont, Size.Empty, mflags);
        int bmpAdv = bmpE2.Width - bmpE1.Width;
        _bmpEmojiCharWidth = bmpAdv > 0 ? bmpAdv : Math.Max(_charWidth, bmpE1.Width);

        // Supplementary emoji (U+1F600 😀 — surrogate pair)
        Size suppE1 = TextRenderer.MeasureText(g, "\U0001F600", _editorFont, Size.Empty, mflags);
        Size suppE2 = TextRenderer.MeasureText(g, "\U0001F600\U0001F600", _editorFont, Size.Empty, mflags);
        int suppAdv = suppE2.Width - suppE1.Width;
        _suppEmojiCharWidth = suppAdv > 0 ? suppAdv : Math.Max(_charWidth, suppE1.Width);

        // Cache a Segoe UI Emoji font for keycap sequences — GDI's per-character
        // font fallback can't compose digit + U+20E3 when the base font is monospace.
        _emojiFallbackFont?.Dispose();
        try { _emojiFallbackFont = new Font("Segoe UI Emoji", _editorFont.Size, FontStyle.Regular, _editorFont.Unit); }
        catch { _emojiFallbackFont = null; }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Painting
    // ────────────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.Clear(_theme.EditorBackground);

        if (_document is null || _lineHeight == 0) return;

        long firstVisible = _scroll?.FirstVisibleLine ?? 0;
        int hOffset = _wordWrap ? 0 : (_scroll?.HorizontalScrollOffset ?? 0);
        int visibleCount = VisibleLineCount + 1; // +1 for partial lines

        long totalDocLines = _document.LineCount;
        long caretLine = _caret?.Line ?? -1;

        // ── Pass 1: Determine which document lines are visible ──────────
        int entryCount = 0;
        long minDocLine = long.MaxValue, maxDocLine = long.MinValue;
        var docLines = new long[visibleCount + 1];

        // When word-wrap is on, firstVisible is a wrap-row index.
        // Map it to the starting document line and the wrap-row offset within that line.
        int firstLineWrapOffset = 0;
        if (_wordWrap)
        {
            var (startDocLine, wrapOff) = WrapRowToDocumentLine(firstVisible);
            firstLineWrapOffset = wrapOff;

            // Collect document lines that contribute wrap rows to the visible area.
            int wrapRowsBudget = visibleCount;
            int wrapCols0 = WrapColumns;
            for (long dl = startDocLine; dl < totalDocLines && wrapRowsBudget > 0; dl++)
            {
                if (_folding is not null && !_folding.IsLineVisible(dl))
                    continue;

                docLines[entryCount++] = dl;
                if (dl < minDocLine) minDocLine = dl;
                if (dl > maxDocLine) maxDocLine = dl;

                // Use GetLineLength for O(1) row count (avoids fetching full line text).
                long lineLen = _document.GetLineLength(dl);
                int rows = lineLen <= wrapCols0 ? 1
                    : Math.Max(1, (int)((lineLen + wrapCols0 - 1) / wrapCols0));
                // For the first line, only remaining rows after the offset count.
                int usedRows = (dl == startDocLine) ? rows - wrapOff : rows;
                wrapRowsBudget -= usedRows;

                if (entryCount >= docLines.Length) break;
            }
        }
        else
        {
            for (int i = 0; i < visibleCount; i++)
            {
                long docLine = _folding is not null
                    ? _folding.VisibleLineToDocumentLine(firstVisible + i)
                    : firstVisible + i;

                if (docLine >= totalDocLines) break;

                docLines[entryCount++] = docLine;
                if (docLine < minDocLine) minDocLine = docLine;
                if (docLine > maxDocLine) maxDocLine = docLine;
            }
        }

        if (entryCount == 0)
        {
            RenderCaret(g, firstVisible, hOffset);
            return;
        }

        // ── Pass 2: Batch-fetch all lines in [min..max] ────────────────
        int rangeCount = (int)(maxDocLine - minDocLine + 1);
        var lineData = _document.GetLineRange(minDocLine, rangeCount);

        // ── Pre-create reusable GDI brushes ─────────────────────────────
        using var hlBrush = new SolidBrush(_theme.LineHighlight);
        using var selBrush = new SolidBrush(_theme.SelectionBackground);
        using var matchBrush = new SolidBrush(_theme.MatchHighlight);

        // ── Pass 3: Render each visible line ────────────────────────────
        int visualRow = 0;
        for (int i = 0; i < entryCount; i++)
        {
            long docLine = docLines[i];
            int dataIndex = (int)(docLine - minDocLine);
            if (dataIndex < 0 || dataIndex >= lineData.Length) continue;

            var (lineText, lineStartOffset) = lineData[dataIndex];

            if (_wordWrap)
            {
                int wrapCols = WrapColumns;
                // Determine which wrap row to start rendering from within this line.
                int startRow = (i == 0) ? firstLineWrapOffset : 0;

                // For very long lines, window the text around the visible area
                // to avoid ExpandTabs / rendering on millions of characters.
                int charsPerScreen = (VisibleLineCount + 4) * wrapCols;
                string wrapText;
                int charClipStart = 0;
                int expandedClipStart = 0;

                if (lineText.Length > charsPerScreen * 2)
                {
                    // Approximate character position for startRow.
                    // For tab-free text (typical JSON) this is exact.
                    charClipStart = Math.Min(startRow * wrapCols, lineText.Length);
                    int charClipEnd = Math.Min(charClipStart + charsPerScreen, lineText.Length);
                    wrapText = lineText.Substring(charClipStart, charClipEnd - charClipStart);
                    expandedClipStart = startRow * wrapCols;
                    startRow = 0; // the clipped text starts at the row we want
                }
                else
                {
                    wrapText = lineText;
                }

                string expanded = ExpandTabs(wrapText);
                int rowsForClip = Math.Max(1, (expanded.Length + wrapCols - 1) / wrapCols);
                List<Token>? tokens = _tokenCache?.GetCachedTokens(docLine);

                // Filter and shift tokens into the clipped window.
                if (tokens is not null && tokens.Count > 0 && wrapText.Length < lineText.Length)
                {
                    var adjusted = new List<Token>();
                    int clipEnd = charClipStart + wrapText.Length;
                    foreach (var t in tokens)
                    {
                        if (t.End <= charClipStart || t.Start >= clipEnd) continue;
                        int aS = Math.Max(0, t.Start - charClipStart);
                        int aE = Math.Min(wrapText.Length, t.End - charClipStart);
                        adjusted.Add(new Token(aS, aE - aS, t.Type));
                    }
                    tokens = adjusted;
                }

                // Evaluate custom highlighting once per line (before the row loop).
                CustomLineResult? wrapCustomResult = null;
                if (CustomHighlightMatcher is not null)
                    wrapCustomResult = CustomHighlightMatcher.MatchLine(wrapText);

                // Compute block state once per document line.
                Color wrapBlockFg = Color.Empty;
                BlockRegion? wrapBlock = null;
                if (CustomBlockRegions is not null && CustomBlockRegions.Count > 0)
                    wrapBlock = Highlighting.CustomHighlightMatcher.GetBlockForLine(CustomBlockRegions, docLine);
                if (wrapBlock.HasValue)
                    wrapBlockFg = wrapBlock.Value.Foreground;

                // Apply block foreground fallback for wrap path.
                if (wrapBlockFg != Color.Empty)
                {
                    if (wrapCustomResult.HasValue && wrapCustomResult.Value.LineForeground == Color.Empty)
                        wrapCustomResult = new CustomLineResult
                        {
                            LineBackground = wrapCustomResult.Value.LineBackground,
                            LineForeground = wrapBlockFg,
                            Spans = wrapCustomResult.Value.Spans,
                        };
                    else if (!wrapCustomResult.HasValue)
                        wrapCustomResult = new CustomLineResult { LineForeground = wrapBlockFg };
                }

                for (int row = startRow; row < rowsForClip; row++)
                {
                    int y = visualRow * _lineHeight;
                    if (y > ClientSize.Height) break;

                    int segStart = row * wrapCols;
                    int segLen = Math.Min(wrapCols, expanded.Length - segStart);

                    // Block background (lowest priority — painted first per row).
                    if (wrapBlock.HasValue && wrapBlock.Value.Background != Color.Empty)
                    {
                        using var blockBgBrush = new SolidBrush(wrapBlock.Value.Background);
                        g.FillRectangle(blockBgBrush, 0, y, ClientSize.Width, _lineHeight);
                    }

                    // Highlight current line (all wrap rows).
                    if (docLine == caretLine)
                        g.FillRectangle(hlBrush, 0, y, ClientSize.Width, _lineHeight);

                    // Render diff background (if in diff mode).
                    if (DiffLineMarkers is not null && row == startRow && firstLineWrapOffset == 0)
                        RenderDiffBackground(g, docLine, y, 0, lineText);

                    // Custom line background (fills entire visual row).
                    if (wrapCustomResult.HasValue && wrapCustomResult.Value.LineBackground != Color.Empty)
                    {
                        using var customBgBrush = new SolidBrush(wrapCustomResult.Value.LineBackground);
                        g.FillRectangle(customBgBrush, 0, y, ClientSize.Width, _lineHeight);
                    }

                    // Paint match-rule span backgrounds BEFORE selection.
                    if (wrapCustomResult.HasValue)
                        RenderCustomSpanBackgroundsWrap(g, wrapText, expanded, segStart, segLen, y, wrapCustomResult.Value);

                    // Selection for this wrap segment.
                    // Map segment positions back to original line coordinates.
                    int origSegStart = segStart + expandedClipStart;
                    int origSegLen = segLen;
                    RenderWrapSelectionBackground(g, lineStartOffset, lineText, expanded,
                        segStart, origSegStart, origSegLen, y, selBrush);

                    // Column selection for this wrap segment.
                    RenderColumnSelectionBackgroundWrap(g, docLine,
                        origSegStart, segLen, y, selBrush);

                    // Text rendering.
                    if (segLen > 0)
                    {
                        string segment = expanded.Substring(segStart, segLen);
                        if (wrapCustomResult.HasValue)
                            RenderCustomHighlightedWrapSegment(g, wrapText, expanded, segStart, segLen, y, wrapCustomResult.Value);
                        else if (tokens is not null && tokens.Count > 0)
                            RenderTokenizedWrapSegment(g, wrapText, expanded, tokens, segStart, segLen, y);
                        else
                            DrawTextAligned(g, segment, _editorFont, TextLeftPadding, y, _theme.EditorForeground);
                    }

                    // Render whitespace glyphs if enabled.
                    if (_showWhitespace)
                    {
                        bool isLastSegment = (row == rowsForClip - 1) && (charClipStart + wrapText.Length >= lineText.Length);
                        RenderWhitespaceWrap(g, wrapText, expanded, segStart, segLen, y,
                            docLine == totalDocLines - 1, isLastSegment);
                    }

                    visualRow++;
                }
            }
            else
            {
                int y = visualRow * _lineHeight;

                // For extremely long lines, extract a window around the
                // visible columns instead of processing millions of chars.
                // All rendering methods use ExpandedColumn which, for
                // tab-free text (typical for long lines), equals the char
                // index. Using renderHOffset = hOffset - clipStart makes
                // all position math work transparently.
                string renderText = lineText;
                int clipStart = 0;
                int renderHOffset = hOffset;
                long renderLineStart = lineStartOffset;
                int visibleWindow = MaxVisibleColumns + 500;

                if (lineText.Length > visibleWindow * 2)
                {
                    clipStart = Math.Clamp(hOffset - 200, 0, lineText.Length);
                    int clipEnd = Math.Clamp(hOffset + visibleWindow, clipStart, lineText.Length);
                    renderText = lineText.Substring(clipStart, clipEnd - clipStart);
                    renderHOffset = hOffset - clipStart;
                    renderLineStart = lineStartOffset + clipStart;
                }

                // Block background (lowest priority — painted first).
                Color blockFg = Color.Empty;
                if (CustomBlockRegions is not null && CustomBlockRegions.Count > 0)
                {
                    var block = Highlighting.CustomHighlightMatcher.GetBlockForLine(CustomBlockRegions, docLine);
                    if (block.HasValue)
                    {
                        blockFg = block.Value.Foreground;
                        if (block.Value.Background != Color.Empty)
                        {
                            using var blockBgBrush = new SolidBrush(block.Value.Background);
                            g.FillRectangle(blockBgBrush, 0, y, ClientSize.Width, _lineHeight);
                        }
                    }
                }

                // Highlight current line.
                if (docLine == caretLine)
                    g.FillRectangle(hlBrush, 0, y, ClientSize.Width, _lineHeight);

                // Render diff background (if in diff mode).
                if (DiffLineMarkers is not null)
                    RenderDiffBackground(g, docLine, y, renderHOffset, renderText);

                // Custom highlighting: evaluate rules for this line.
                CustomLineResult? customResult = null;
                if (CustomHighlightMatcher is not null)
                {
                    customResult = CustomHighlightMatcher.MatchLine(renderText);
                    if (customResult.Value.LineBackground != Color.Empty)
                    {
                        using var customBgBrush = new SolidBrush(customResult.Value.LineBackground);
                        g.FillRectangle(customBgBrush, 0, y, ClientSize.Width, _lineHeight);
                    }
                    // Paint match-rule span backgrounds BEFORE selection so
                    // selection is visible on top.
                    RenderCustomSpanBackgrounds(g, renderText, y, renderHOffset, customResult.Value);
                }

                // Apply block foreground fallback when no line-rule foreground is set.
                if (blockFg != Color.Empty)
                {
                    if (customResult.HasValue && customResult.Value.LineForeground == Color.Empty)
                        customResult = new CustomLineResult
                        {
                            LineBackground = customResult.Value.LineBackground,
                            LineForeground = blockFg,
                            Spans = customResult.Value.Spans,
                        };
                    else if (!customResult.HasValue)
                        customResult = new CustomLineResult { LineForeground = blockFg };
                }

                // Render search match highlights for this line.
                RenderMatchHighlights(g, renderText, y, renderHOffset, matchBrush);

                // Render selection background for this line.
                RenderSelectionBackground(g, renderText, renderLineStart, y, renderHOffset, selBrush);

                // Render column selection background for this line.
                RenderColumnSelectionBackground(g, renderText, docLine, y, renderHOffset, selBrush);

                if (customResult.HasValue)
                {
                    RenderCustomHighlightedLine(g, renderText, y, renderHOffset, customResult.Value);
                }
                else
                {
                    // Get tokens for syntax highlighting.
                    List<Token>? tokens = _tokenCache?.GetCachedTokens(docLine);

                    // When the line was clipped, filter and shift tokens into the window.
                    if (tokens is not null && tokens.Count > 0 && renderText.Length < lineText.Length)
                    {
                        var adjusted = new List<Token>();
                        int clipEnd = clipStart + renderText.Length;
                        foreach (var t in tokens)
                        {
                            if (t.End <= clipStart || t.Start >= clipEnd) continue;
                            int adjS = Math.Max(0, t.Start - clipStart);
                            int adjE = Math.Min(renderText.Length, t.End - clipStart);
                            adjusted.Add(new Token(adjS, adjE - adjS, t.Type));
                        }
                        tokens = adjusted;
                    }

                    if (tokens is not null && tokens.Count > 0)
                        RenderTokenizedLine(g, renderText, tokens, y, renderHOffset);
                    else
                        RenderPlainLine(g, renderText, y, renderHOffset);
                }

                // Render whitespace glyphs if enabled.
                if (_showWhitespace)
                    RenderWhitespace(g, renderText, y, renderHOffset, docLine == totalDocLines - 1);

                // Render fold ellipsis indicator if next lines are collapsed.
                if (_folding is not null && _folding.IsFoldStart(docLine) && _folding.IsCollapsed(docLine))
                {
                    // Use original line length for fold ellipsis position.
                    string expandedFold = ExpandTabs(lineText);
                    int textEndX = DisplayX(expandedFold, hOffset, expandedFold.Length) + TextLeftPadding;
                    string indicator = " ... ";
                    using var bgBrush = new SolidBrush(Color.FromArgb(EditorControl.DefaultFoldIndicatorOpacity, 128, 128, 128));
                    int indicatorWidth = indicator.Length * _charWidth;
                    g.FillRectangle(bgBrush, textEndX + 4, y, indicatorWidth, _lineHeight);
                    TextRenderer.DrawText(g, indicator, _editorFont,
                        new Point(textEndX + 4, y), _theme.GutterForeground, DrawFlags);
                }

                visualRow++;
            }
        }

        // Render caret.
        if (_wordWrap)
            RenderCaretWrapped(g, firstVisible, firstLineWrapOffset, lineData, minDocLine, docLines, entryCount);
        else
            RenderCaret(g, firstVisible, hOffset);
    }

    private void RenderPlainLine(Graphics g, string lineText, int y, int hOffset)
    {
        string expanded = ExpandTabs(lineText);
        int startCol = hOffset;

        if (startCol >= expanded.Length) return;

        string visible = expanded.Substring(startCol,
            Math.Min(MaxVisibleColumns + 1, expanded.Length - startCol));

        DrawTextAligned(g, visible, _editorFont, TextLeftPadding, y, _theme.EditorForeground);
    }

    private void RenderTokenizedLine(Graphics g, string lineText, List<Token> tokens, int y, int hOffset)
    {
        // Expand tabs for accurate column positioning.
        string expanded = ExpandTabs(lineText);

        foreach (Token token in tokens)
        {
            int tokenStart = ExpandedColumn(lineText, token.Start);
            int tokenEnd = ExpandedColumn(lineText, token.End);

            // Clip to visible character range.
            int srcStart = Math.Max(tokenStart, hOffset);
            int srcEnd = Math.Min(tokenEnd, hOffset + MaxVisibleColumns + 1);

            if (srcStart >= expanded.Length || srcEnd <= srcStart) continue;

            // Pixel-level visibility check.
            int px = DisplayX(expanded, hOffset, srcStart);
            if (px > ClientSize.Width) continue;

            string fragment = expanded.Substring(srcStart,
                Math.Min(srcEnd - srcStart, expanded.Length - srcStart));

            int x = px + TextLeftPadding;
            Color color = _theme.GetTokenColor(token.Type);

            DrawTextAligned(g, fragment, _editorFont, x, y, color);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Custom highlighting rendering
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Paints match-rule span backgrounds only (no text).
    /// Called BEFORE selection so selection draws on top.
    /// </summary>
    private void RenderCustomSpanBackgrounds(Graphics g, string lineText, int y, int hOffset, CustomLineResult result)
    {
        if (result.Spans is null or { Count: 0 }) return;

        int startCol = hOffset;
        int endCol = Math.Min(hOffset + MaxVisibleColumns + 1, ExpandTabs(lineText).Length);

        string expanded = ExpandTabs(lineText);
        foreach (var span in result.Spans)
        {
            if (span.Background == Color.Empty) continue;
            int expStart = ExpandedColumn(lineText, span.Start);
            int expEnd = ExpandedColumn(lineText, span.Start + span.Length);
            int drawStart = Math.Max(expStart, startCol);
            int drawEnd = Math.Min(expEnd, endCol);
            if (drawStart >= drawEnd) continue;

            int x = DisplayX(expanded, hOffset, drawStart) + TextLeftPadding;
            int w = DisplayX(expanded, drawStart, drawEnd);
            using var bgBrush = new SolidBrush(span.Background);
            g.FillRectangle(bgBrush, x, y, w, _lineHeight);
        }
    }

    /// <summary>
    /// Paints match-rule span backgrounds for a word-wrap segment (no text).
    /// Called BEFORE selection so selection draws on top.
    /// </summary>
    private void RenderCustomSpanBackgroundsWrap(Graphics g, string lineText, string expanded,
        int segStart, int segLen, int y, CustomLineResult result)
    {
        if (result.Spans is null or { Count: 0 }) return;
        int segEnd = segStart + segLen;

        foreach (var span in result.Spans)
        {
            if (span.Background == Color.Empty) continue;
            int expStart = ExpandedColumn(lineText, span.Start);
            int expEnd = ExpandedColumn(lineText, span.Start + span.Length);
            int drawStart = Math.Max(expStart, segStart);
            int drawEnd = Math.Min(expEnd, segEnd);
            if (drawStart >= drawEnd) continue;

            int x = DisplayX(expanded, segStart, drawStart) + TextLeftPadding;
            int w = DisplayX(expanded, drawStart, drawEnd);
            using var bgBrush = new SolidBrush(span.Background);
            g.FillRectangle(bgBrush, x, y, w, _lineHeight);
        }
    }

    /// <summary>
    /// Renders custom-highlighted text (foreground only — backgrounds are
    /// already painted by <see cref="RenderCustomSpanBackgrounds"/>).
    /// </summary>
    private void RenderCustomHighlightedLine(Graphics g, string lineText, int y, int hOffset, CustomLineResult result)
    {
        string expanded = ExpandTabs(lineText);
        int startCol = hOffset;
        int endCol = Math.Min(hOffset + MaxVisibleColumns + 1, expanded.Length);
        if (startCol >= expanded.Length) return;

        Color defaultFg = result.LineForeground != Color.Empty
            ? result.LineForeground : _theme.EditorForeground;

        if (result.Spans is null or { Count: 0 })
        {
            string visible = expanded.Substring(startCol, endCol - startCol);
            DrawTextAligned(g, visible, _editorFont, TextLeftPadding, y, defaultFg);
            return;
        }

        // Build expanded-column spans from raw-char spans.
        var expandedSpans = new List<(int Start, int End, Color Fg)>(result.Spans.Count);
        foreach (var span in result.Spans)
        {
            int expStart = ExpandedColumn(lineText, span.Start);
            int expEnd = ExpandedColumn(lineText, span.Start + span.Length);
            if (expEnd <= startCol || expStart >= endCol) continue;
            expandedSpans.Add((expStart, expEnd, span.Foreground));
        }

        // Render text segment-by-segment: gaps in default fg, spans in their fg.
        int cursor = startCol;
        foreach (var (spanStart, spanEnd, spanFg) in expandedSpans)
        {
            if (cursor < spanStart)
                RenderTextSegment(g, expanded, cursor, Math.Min(spanStart, endCol), hOffset, y, defaultFg);

            int drawStart = Math.Max(cursor, spanStart);
            int drawEnd = Math.Min(spanEnd, endCol);
            if (drawStart < drawEnd)
            {
                Color fg = spanFg != Color.Empty ? spanFg : defaultFg;
                RenderTextSegment(g, expanded, drawStart, drawEnd, hOffset, y, fg);
            }

            cursor = Math.Max(cursor, spanEnd);
        }

        if (cursor < endCol)
            RenderTextSegment(g, expanded, cursor, endCol, hOffset, y, defaultFg);
    }

    /// <summary>
    /// Renders custom-highlighted text for a word-wrap segment (foreground only).
    /// </summary>
    private void RenderCustomHighlightedWrapSegment(Graphics g, string lineText, string expanded,
        int segStart, int segLen, int y, CustomLineResult result)
    {
        int segEnd = segStart + segLen;

        Color defaultFg = result.LineForeground != Color.Empty
            ? result.LineForeground : _theme.EditorForeground;

        if (result.Spans is null or { Count: 0 })
        {
            string segment = expanded.Substring(segStart, segLen);
            DrawTextAligned(g, segment, _editorFont, TextLeftPadding, y, defaultFg);
            return;
        }

        // Build expanded-column spans clipped to this segment.
        var expandedSpans = new List<(int Start, int End, Color Fg)>(result.Spans.Count);
        foreach (var span in result.Spans)
        {
            int expStart = ExpandedColumn(lineText, span.Start);
            int expEnd = ExpandedColumn(lineText, span.Start + span.Length);
            if (expEnd <= segStart || expStart >= segEnd) continue;
            expandedSpans.Add((expStart, expEnd, span.Foreground));
        }

        int cursor = segStart;
        foreach (var (spanStart, spanEnd, spanFg) in expandedSpans)
        {
            if (cursor < spanStart && cursor < segEnd)
            {
                int gapEnd = Math.Min(spanStart, segEnd);
                string fragment = expanded.Substring(cursor, gapEnd - cursor);
                int x = DisplayX(expanded, segStart, cursor) + TextLeftPadding;
                DrawTextAligned(g, fragment, _editorFont, x, y, defaultFg);
            }

            int drawStart = Math.Max(cursor, spanStart);
            int drawEnd = Math.Min(spanEnd, segEnd);
            if (drawStart < drawEnd)
            {
                string fragment = expanded.Substring(drawStart, drawEnd - drawStart);
                int x = DisplayX(expanded, segStart, drawStart) + TextLeftPadding;
                Color fg = spanFg != Color.Empty ? spanFg : defaultFg;
                DrawTextAligned(g, fragment, _editorFont, x, y, fg);
            }

            cursor = Math.Max(cursor, spanEnd);
        }

        if (cursor < segEnd)
        {
            string fragment = expanded.Substring(cursor, segEnd - cursor);
            int x = DisplayX(expanded, segStart, cursor) + TextLeftPadding;
            DrawTextAligned(g, fragment, _editorFont, x, y, defaultFg);
        }
    }

    private void RenderTextSegment(Graphics g, string expanded, int colStart, int colEnd,
        int hOffset, int y, Color fgColor)
    {
        if (colStart >= colEnd || colStart >= expanded.Length) return;

        int drawEnd = Math.Min(colEnd, expanded.Length);
        string fragment = expanded.Substring(colStart, drawEnd - colStart);
        int x = DisplayX(expanded, hOffset, colStart) + TextLeftPadding;

        DrawTextAligned(g, fragment, _editorFont, x, y, fgColor);
    }

    private void RenderSelectionBackground(Graphics g, string lineText, long lineStartOffset, int y, int hOffset, Brush selBrush)
    {
        if (_selection is null || !_selection.HasSelection) return;

        long lineStart = lineStartOffset;
        long lineEnd = lineStart + lineText.Length;

        long selStart = _selection.SelectionStart;
        long selEnd = _selection.SelectionEnd;

        if (selEnd <= lineStart || selStart >= lineEnd + 1) return;

        long drawSelStart = Math.Max(selStart, lineStart) - lineStart;
        long drawSelEnd = Math.Min(selEnd, lineEnd) - lineStart;

        string expanded = ExpandTabs(lineText);
        int charStart = ExpandedColumn(lineText, (int)drawSelStart);
        int charEnd = ExpandedColumn(lineText, (int)drawSelEnd);

        int x1 = Math.Max(0, DisplayX(expanded, hOffset, charStart)) + TextLeftPadding;
        int x2 = Math.Max(x1, DisplayX(expanded, hOffset, charEnd) + TextLeftPadding);

        // If selection extends past line end (i.e. includes the newline), extend slightly.
        if (selEnd > lineEnd)
            x2 = Math.Max(x2, x2 + _charWidth);

        g.FillRectangle(selBrush, x1, y, x2 - x1, _lineHeight);
    }

    private void RenderColumnSelectionBackground(Graphics g, string lineText,
        long docLine, int y, int hOffset, Brush selBrush)
    {
        if (_selection is null || !_selection.HasColumnSelection) return;
        if (docLine < _selection.ColumnStartLine || docLine > _selection.ColumnEndLine) return;

        // Column selection stores *visual* columns (narrow=1, wide=2, zero-width=0).
        // Use uniform _charWidth per visual column so the rectangle is perfectly
        // rectangular across all lines, regardless of character composition.
        int leftVisCol = (int)_selection.ColumnLeftCol;
        int rightVisCol = (int)_selection.ColumnRightCol;

        // Convert horizontal scroll offset (char index) to visual column.
        string expanded = ExpandTabs(lineText);
        int scrollVisCol = (hOffset <= 0) ? 0
            : CharIndexToVisualColumn(expanded, Math.Min(hOffset, expanded.Length));

        int x1px = (leftVisCol - scrollVisCol) * _charWidth;
        int x2px = (rightVisCol - scrollVisCol) * _charWidth;

        int x1 = Math.Max(0, x1px) + TextLeftPadding;
        int x2 = Math.Max(x1, x2px + TextLeftPadding);

        if (x2 > x1)
            g.FillRectangle(selBrush, x1, y, x2 - x1, _lineHeight);
    }

    private void RenderColumnSelectionBackgroundWrap(Graphics g,
        long docLine, int segStartExpanded, int segLen, int y, Brush selBrush)
    {
        if (_selection is null || !_selection.HasColumnSelection) return;
        if (docLine < _selection.ColumnStartLine || docLine > _selection.ColumnEndLine) return;

        // Column selection stores *absolute* expanded columns from line start.
        // The wrap segment covers absolute columns [segStartExpanded, segStartExpanded + segLen).
        // Intersect the selection range with the segment range.
        int leftVisCol = (int)_selection.ColumnLeftCol;
        int rightVisCol = (int)_selection.ColumnRightCol;

        int selLeft = Math.Max(leftVisCol, segStartExpanded);
        int selRight = Math.Min(rightVisCol, segStartExpanded + segLen);
        if (selRight <= selLeft) return;

        // Convert absolute columns to pixel positions relative to the segment start.
        int x1 = (selLeft - segStartExpanded) * _charWidth + TextLeftPadding;
        int x2 = (selRight - segStartExpanded) * _charWidth + TextLeftPadding;
        g.FillRectangle(selBrush, x1, y, x2 - x1, _lineHeight);
    }

    private void RenderMatchHighlights(Graphics g, string lineText, int y, int hOffset, Brush matchBrush)
    {
        if (SearchHighlightPattern is null) return;

        string expanded = ExpandTabs(lineText);
        foreach (System.Text.RegularExpressions.Match m in SearchHighlightPattern.Matches(lineText))
        {
            if (m.Length == 0) continue;

            int charStart = ExpandedColumn(lineText, m.Index);
            int charEnd = ExpandedColumn(lineText, m.Index + m.Length);

            int x1 = Math.Max(0, DisplayX(expanded, hOffset, charStart)) + TextLeftPadding;
            int x2 = Math.Max(x1, DisplayX(expanded, hOffset, charEnd) + TextLeftPadding);

            g.FillRectangle(matchBrush, x1, y, x2 - x1, _lineHeight);
        }
    }

    private void RenderDiffBackground(Graphics g, long docLine, int y, int hOffset, string lineText)
    {
        if (DiffLineMarkers is null || docLine < 0 || docLine >= DiffLineMarkers.Length)
            return;

        DiffLine marker = DiffLineMarkers[docLine];
        Color? bgColor = marker.Type switch
        {
            DiffLineType.Added    => _theme.DiffAddedBackground,
            DiffLineType.Removed  => _theme.DiffRemovedBackground,
            DiffLineType.Modified => _theme.DiffModifiedBackground,
            DiffLineType.Padding  => _theme.DiffPaddingBackground,
            _ => null,
        };

        if (bgColor.HasValue)
        {
            using var brush = new SolidBrush(bgColor.Value);
            g.FillRectangle(brush, 0, y, ClientSize.Width, _lineHeight);
        }

        // Character-level highlights.
        if (marker.CharDiffs is { Count: > 0 })
        {
            string expanded = ExpandTabs(lineText);
            using var charBrush = new SolidBrush(_theme.DiffModifiedCharBackground);
            foreach (var range in marker.CharDiffs)
            {
                int startCol = ExpandedColumn(lineText, Math.Min(range.Start, lineText.Length));
                int endCol = ExpandedColumn(lineText, Math.Min(range.Start + range.Length, lineText.Length));

                int x1 = DisplayX(expanded, hOffset, startCol) + TextLeftPadding;
                int x2 = DisplayX(expanded, hOffset, endCol) + TextLeftPadding;

                if (x2 > x1 && x2 > 0)
                    g.FillRectangle(charBrush, Math.Max(0, x1), y, x2 - Math.Max(0, x1), _lineHeight);
            }
        }
    }

    private void RenderCaret(Graphics g, long firstVisibleLine, int hOffset)
    {
        if (_caret is null || !_caret.IsVisible) return;

        long caretLine = _caret.Line;
        long caretCol = _caret.Column;

        long visibleLine;
        if (_folding is not null)
            visibleLine = _folding.DocumentLineToVisibleLine(caretLine);
        else
            visibleLine = caretLine;

        if (visibleLine < firstVisibleLine ||
            visibleLine >= firstVisibleLine + VisibleLineCount)
            return;

        int y = (int)(visibleLine - firstVisibleLine) * _lineHeight;

        // Expand tabs to get the correct pixel position.
        string lineText = _document is not null && caretLine < _document.LineCount
            ? _document.GetLine(caretLine)
            : string.Empty;

        string expanded = ExpandTabs(lineText);
        int expandedCol = ExpandedColumn(lineText, (int)Math.Min(caretCol, lineText.Length));
        int x = DisplayX(expanded, hOffset, expandedCol) + TextLeftPadding;

        if (x < 0 || x > ClientSize.Width) return;

        using var caretPen = new Pen(_theme.CaretColor, 2);
        g.DrawLine(caretPen, x, y, x, y + _lineHeight);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Word wrap helpers
    // ────────────────────────────────────────────────────────────────────

    private void RenderTokenizedWrapSegment(Graphics g, string lineText, string expanded,
        List<Token> tokens, int segStart, int segLen, int y)
    {
        int segEnd = segStart + segLen;

        foreach (Token token in tokens)
        {
            // Convert raw char indices to expanded column positions.
            int tokStart = ExpandedColumn(lineText, token.Start);
            int tokEnd = ExpandedColumn(lineText, token.End);

            // Clamp to this segment.
            int drawStart = Math.Max(tokStart, segStart);
            int drawEnd = Math.Min(tokEnd, segEnd);

            if (drawStart >= drawEnd) continue;

            string fragment = expanded.Substring(drawStart,
                Math.Min(drawEnd - drawStart, expanded.Length - drawStart));
            int x = DisplayX(expanded, segStart, drawStart) + TextLeftPadding;
            Color color = _theme.GetTokenColor(token.Type);

            DrawTextAligned(g, fragment, _editorFont, x, y, color);
        }
    }

    private void RenderWrapSelectionBackground(Graphics g, long lineStartOffset,
        string lineText, string expanded, int localSegStart, int segStartExpanded, int segLen, int y, Brush selBrush)
    {
        if (_selection is null || !_selection.HasSelection) return;

        long lineStart = lineStartOffset;
        long lineEnd = lineStart + lineText.Length;

        long selStart = _selection.SelectionStart;
        long selEnd = _selection.SelectionEnd;

        if (selEnd <= lineStart || selStart >= lineEnd + 1) return;

        // Get selection range within line in expanded column space.
        long localSelStart = Math.Max(selStart - lineStart, 0);
        long localSelEnd = Math.Min(selEnd - lineStart, lineText.Length);

        int expSelStart = ExpandedColumn(lineText, (int)localSelStart);
        int expSelEnd = ExpandedColumn(lineText, (int)localSelEnd);

        // Clamp to this wrap segment.
        int segEnd = segStartExpanded + segLen;
        int drawStart = Math.Max(expSelStart, segStartExpanded) - segStartExpanded;
        int drawEnd = Math.Min(expSelEnd, segEnd) - segStartExpanded;

        // If selection extends past line end and this is the last segment.
        if (selEnd > lineEnd && segEnd >= ExpandedColumn(lineText, lineText.Length))
            drawEnd = Math.Max(drawEnd, drawEnd + 1);

        if (drawEnd <= drawStart) return;

        int x1 = DisplayX(expanded, localSegStart, localSegStart + drawStart) + TextLeftPadding;
        int x2 = DisplayX(expanded, localSegStart, localSegStart + drawEnd) + TextLeftPadding;
        g.FillRectangle(selBrush, x1, y, x2 - x1, _lineHeight);
    }

    private void RenderCaretWrapped(Graphics g, long firstVisibleLine,
        int firstLineWrapOffset,
        (string Text, long StartOffset)[] lineData, long minDocLine,
        long[] docLines, int entryCount)
    {
        if (_caret is null || !_caret.IsVisible || _document is null) return;

        long caretLine = _caret.Line;
        long caretCol = _caret.Column;
        int wrapCols = WrapColumns;

        // Find the visual row for the caret.
        int visualRow = 0;
        for (int i = 0; i < entryCount; i++)
        {
            long docLine = docLines[i];
            int dataIndex = (int)(docLine - minDocLine);
            if (dataIndex < 0 || dataIndex >= lineData.Length)
            {
                visualRow++;
                continue;
            }

            string lineText = lineData[dataIndex].Text;
            int startRow = (i == 0) ? firstLineWrapOffset : 0;

            // For very long lines, compute wrap row from the caret's expanded column
            // without ExpandTabs on the full line.
            int expandedLineLen = lineText.Length; // approximate for tab-free text
            int rowsForLine = Math.Max(1, (expandedLineLen + wrapCols - 1) / wrapCols);
            int renderedRows = rowsForLine - startRow;

            if (docLine == caretLine)
            {
                int expandedCol = ExpandedColumn(lineText, (int)Math.Min(caretCol, lineText.Length));
                int wrapRow = wrapCols > 0 ? expandedCol / wrapCols : 0;
                int colInRow = expandedCol - wrapRow * wrapCols;

                // The caret's wrap row relative to the first rendered row.
                int caretVisualRow = visualRow + wrapRow - startRow;

                int y = caretVisualRow * _lineHeight;
                string expandedStr = ExpandTabs(lineText);
                int rowStart = wrapRow * wrapCols;
                int x = DisplayX(expandedStr, rowStart, Math.Min(rowStart + colInRow, expandedStr.Length)) + TextLeftPadding;

                if (y >= 0 && y <= ClientSize.Height)
                {
                    using var caretPen = new Pen(_theme.CaretColor, 2);
                    g.DrawLine(caretPen, x, y, x, y + _lineHeight);
                }
                return;
            }

            visualRow += renderedRows;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Whitespace rendering
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders visible whitespace glyphs (middle dot for spaces, arrow for
    /// tabs, pilcrow for line endings) over the already-drawn text.
    /// </summary>
    private void RenderWhitespace(Graphics g, string lineText, int y, int hOffset, bool isLastLine)
    {
        if (!_showWhitespace) return;

        var wsColor = Color.FromArgb(EditorControl.DefaultWhitespaceOpacity,
            _theme.EditorForeground.R,
            _theme.EditorForeground.G,
            _theme.EditorForeground.B);

        int col = 0;        // character index in expanded string
        int dispPx = 0;     // pixel position from start of line
        int hDispPx = 0;    // pixel position for hOffset
        // Pre-compute pixel offset for hOffset.
        {
            string expanded = ExpandTabs(lineText);
            hDispPx = DisplayX(expanded, 0, Math.Min(hOffset, expanded.Length));
        }

        for (int i = 0; i < lineText.Length; i++)
        {
            char c = lineText[i];
            if (c == '\t')
            {
                int tabWidth = _tabSize - (col % _tabSize);
                int x = dispPx - hDispPx + TextLeftPadding;
                if (x >= 0 && x < ClientSize.Width)
                {
                    TextRenderer.DrawText(g, "\u2192", _editorFont,
                        new Point(x, y), wsColor, DrawFlags);
                }
                col += tabWidth;
                dispPx += tabWidth * _charWidth;
            }
            else if (c == ' ')
            {
                int x = dispPx - hDispPx + TextLeftPadding;
                if (x >= 0 && x < ClientSize.Width)
                {
                    TextRenderer.DrawText(g, "\u00B7", _editorFont,
                        new Point(x, y), wsColor, DrawFlags);
                }
                col++;
                dispPx += _charWidth;
            }
            else if (c == '\r' || c == '\n')
            {
                break;
            }
            else
            {
                int w = GetCharDisplayWidth(lineText, i);
                if (w > 0) // skip low surrogates (w == 0)
                {
                    col++;
                    dispPx += CharPixelWidth(lineText, i, w);
                }
            }
        }

        // Draw line ending indicator (¶) after the last character.
        if (!isLastLine)
        {
            int x = dispPx - hDispPx + TextLeftPadding;
            if (x >= 0 && x < ClientSize.Width)
            {
                TextRenderer.DrawText(g, "\u00B6", _editorFont,
                    new Point(x, y), wsColor, DrawFlags);
            }
        }
    }

    /// <summary>
    /// Renders whitespace glyphs for a word-wrap segment.
    /// </summary>
    private void RenderWhitespaceWrap(Graphics g, string lineText, string expanded,
        int segStart, int segLen, int y, bool isLastLine, bool isLastSegment)
    {
        if (!_showWhitespace) return;

        var wsColor = Color.FromArgb(EditorControl.DefaultWhitespaceOpacity,
            _theme.EditorForeground.R,
            _theme.EditorForeground.G,
            _theme.EditorForeground.B);

        int segEnd = segStart + segLen;

        // Walk the raw lineText and map each char to its expanded column.
        int col = 0;
        for (int i = 0; i < lineText.Length; i++)
        {
            char c = lineText[i];
            if (c == '\r' || c == '\n') break;

            if (c == '\t')
            {
                int tabWidth = _tabSize - (col % _tabSize);
                if (col >= segStart && col < segEnd)
                {
                    int x = DisplayX(expanded, segStart, col) + TextLeftPadding;
                    TextRenderer.DrawText(g, "\u2192", _editorFont,
                        new Point(x, y), wsColor, DrawFlags);
                }
                col += tabWidth;
            }
            else if (c == ' ')
            {
                if (col >= segStart && col < segEnd)
                {
                    int x = DisplayX(expanded, segStart, col) + TextLeftPadding;
                    TextRenderer.DrawText(g, "\u00B7", _editorFont,
                        new Point(x, y), wsColor, DrawFlags);
                }
                col++;
            }
            else
            {
                int w = GetCharDisplayWidth(lineText, i);
                if (w > 0) // skip low surrogates
                    col++;
            }

            if (col >= segEnd) break;
        }

        // Line ending indicator on the last wrap segment.
        if (isLastSegment && !isLastLine)
        {
            // Find expanded length of text content (without line endings).
            int textEndCol = col;
            if (textEndCol >= segStart && textEndCol < segEnd)
            {
                int x = DisplayX(expanded, segStart, textEndCol) + TextLeftPadding;
                TextRenderer.DrawText(g, "\u00B6", _editorFont,
                    new Point(x, y), wsColor, DrawFlags);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Tab expansion helpers
    // ────────────────────────────────────────────────────────────────────

    private string ExpandTabs(string text)
    {
        if (!text.Contains('\t')) return text;

        var sb = new System.Text.StringBuilder(text.Length + 16);
        int col = 0;
        foreach (char c in text)
        {
            if (c == '\t')
            {
                int spaces = _tabSize - (col % _tabSize);
                sb.Append(' ', spaces);
                col += spaces;
            }
            else
            {
                sb.Append(c);
                col++;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Converts a character index in the raw line text to the expanded
    /// column position (accounting for tab stops). Returns a character
    /// index into the tab-expanded string.
    /// </summary>
    private int ExpandedColumn(string text, int charIndex)
    {
        int col = 0;
        int limit = Math.Min(charIndex, text.Length);
        for (int i = 0; i < limit; i++)
        {
            if (text[i] == '\t')
                col += _tabSize - (col % _tabSize);
            else
                col++;
        }
        return col;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Mouse events
    // ────────────────────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || _document is null ||
            _caret is null || _selection is null) return;

        Focus();

        // Click count detection for double/triple click.
        TimeSpan elapsed = DateTime.Now - _lastClickTime;
        if (elapsed.TotalMilliseconds < SystemInformation.DoubleClickTime &&
            Math.Abs(e.X - _lastClickPoint.X) < SystemInformation.DoubleClickSize.Width &&
            Math.Abs(e.Y - _lastClickPoint.Y) < SystemInformation.DoubleClickSize.Height)
        {
            _clickCount++;
        }
        else
        {
            _clickCount = 1;
        }

        _lastClickTime = DateTime.Now;
        _lastClickPoint = e.Location;

        long offset = HitTestOffset(e.X, e.Y);

        if (_clickCount == 2)
        {
            // Double-click: select word.
            _selection.SelectWord(offset);
            _caret.MoveTo(_selection.SelectionEnd);
        }
        else if (_clickCount >= 3)
        {
            // Triple-click: select line.
            var (line, _) = HitTestLineColumn(e.X, e.Y);
            _selection.SelectLine(line);
            _caret.MoveTo(_selection.SelectionEnd);
            _clickCount = 3; // cap
        }
        else
        {
            // Single click.
            if ((ModifierKeys & Keys.Alt) != 0)
            {
                // Alt+click starts column (box) selection using visual columns.
                var (line, expCol) = HitTestLineExpandedColumn(e.X, e.Y);
                _selection.StartColumnSelection(line, expCol);
                // Move caret to the character position (clamped to line length).
                var (_, charCol) = HitTestLineColumn(e.X, e.Y);
                _caret.MoveToLineColumn(line, charCol);
                _mouseDown = true;
                Invalidate();
                return;
            }
            else if ((ModifierKeys & Keys.Shift) != 0)
            {
                // Shift+click extends selection.
                if (!_selection.HasSelection)
                    _selection.StartSelection(_caret.Offset);
                _caret.MoveTo(offset);
                _selection.ExtendSelection(offset);
            }
            else
            {
                _selection.ClearSelection();
                _caret.MoveTo(offset);
                _selection.StartSelection(offset);
            }
        }

        _mouseDown = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_mouseDown || _document is null || _caret is null || _selection is null)
            return;

        if (_selection.IsColumnMode)
        {
            var (line, expCol) = HitTestLineExpandedColumn(e.X, e.Y);
            _selection.ExtendColumnSelection(line, expCol);
            // Move caret to the character position (clamped to line length).
            var (_, charCol) = HitTestLineColumn(e.X, e.Y);
            _caret.MoveToLineColumn(line, charCol);
            Invalidate();
            return;
        }

        long offset = HitTestOffset(e.X, e.Y);
        _caret.MoveTo(offset);
        _selection.ExtendSelection(offset);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _mouseDown = false;
    }

    /// <summary>Raised when Ctrl+MouseWheel requests a zoom change (+1 or -1).</summary>
    public event Action<int>? ZoomRequested;

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if ((ModifierKeys & Keys.Control) != 0)
        {
            // Ctrl+Wheel = zoom.
            ZoomRequested?.Invoke(e.Delta > 0 ? 1 : -1);
            return;
        }

        _scroll?.HandleMouseWheel(e.Delta);
        Invalidate();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Keyboard events
    // ────────────────────────────────────────────────────────────────────

    protected override bool IsInputKey(Keys keyData)
    {
        // Ensure arrow keys, Tab, etc. are sent to the control.
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
            case Keys.Tab:
            case Keys.Home:
            case Keys.End:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Delete:
            case Keys.Back:
            case Keys.Enter:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = e.Control;
        bool shift = e.Shift;
        bool alt = e.Alt;

        if (InputHandler is not null && InputHandler.ProcessKeyDown(e.KeyCode, ctrl, shift, alt))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            _caret?.EnsureVisible(_scroll!, VisibleLineCount, MaxVisibleColumns);
            Invalidate();
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (InputHandler is not null && e.KeyChar >= 32)
        {
            InputHandler.ProcessCharInput(e.KeyChar);
            e.Handled = true;
            _caret?.EnsureVisible(_scroll!, VisibleLineCount, MaxVisibleColumns);
            Invalidate();
        }

        base.OnKeyPress(e);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Hit testing
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a pixel coordinate to a (line, column) pair.
    /// </summary>
    public (long Line, long Column) HitTestLineColumn(int x, int y)
    {
        if (_document is null) return (0, 0);

        long firstVisible = _scroll?.FirstVisibleLine ?? 0;
        int hOffset = _wordWrap ? 0 : (_scroll?.HorizontalScrollOffset ?? 0);

        if (_wordWrap)
            return HitTestLineColumnWrapped(x, y, firstVisible);

        int lineIndex = _lineHeight > 0 ? y / _lineHeight : 0;
        long visibleLine = firstVisible + lineIndex;

        long docLine;
        if (_folding is not null)
            docLine = _folding.VisibleLineToDocumentLine(visibleLine);
        else
            docLine = visibleLine;

        docLine = Math.Clamp(docLine, 0, _document.LineCount - 1);

        // Convert pixel x to character index, accounting for CJK display widths.
        string lineText = _document.GetLine(docLine);
        string expanded = ExpandTabs(lineText);
        int charIndex = CharIndexFromPixel(expanded, hOffset, Math.Max(0, x - TextLeftPadding));
        int col = CompressedColumn(lineText, charIndex);

        return (docLine, col);
    }

    private (long Line, long Column) HitTestLineColumnWrapped(int x, int y, long firstVisible)
    {
        int targetRow = _lineHeight > 0 ? y / _lineHeight : 0;
        int wrapCols = WrapColumns;
        long totalDocLines = _document!.LineCount;

        // firstVisible is a wrap-row index. Map to starting document line.
        var (startDocLine, wrapOff) = WrapRowToDocumentLine(firstVisible);

        int visualRow = 0;
        for (long docLine = startDocLine; docLine < totalDocLines; docLine++)
        {
            if (_folding is not null && !_folding.IsLineVisible(docLine))
                continue;

            string lineText = _document.GetLine(docLine);
            int lineLen = lineText.Length; // approximate expanded length for tab-free text
            int rowsForLine = Math.Max(1, (lineLen + wrapCols - 1) / wrapCols);
            int startRow = (docLine == startDocLine) ? wrapOff : 0;
            int renderedRows = rowsForLine - startRow;

            if (targetRow < visualRow + renderedRows)
            {
                int wrapRow = startRow + (targetRow - visualRow);
                int segStart = wrapRow * wrapCols;
                int expandedCol = CharIndexFromPixel(lineText, segStart, Math.Max(0, x - TextLeftPadding));
                expandedCol = Math.Min(expandedCol, lineLen);
                int col = CompressedColumn(lineText, expandedCol);
                return (docLine, col);
            }

            visualRow += renderedRows;
        }

        // Past end of document.
        long lastLine = Math.Max(0, totalDocLines - 1);
        string lastText = _document.GetLine(lastLine);
        return (lastLine, lastText.Length);
    }

    /// <summary>
    /// Converts a pixel coordinate to a (line, expandedColumn) pair.
    /// Unlike <see cref="HitTestLineColumn"/> the column is NOT compressed
    /// to a character index — it is the raw visual column, which may exceed
    /// the line length. Used for column (box) selection.
    /// </summary>
    public (long Line, int ExpandedColumn) HitTestLineExpandedColumn(int x, int y)
    {
        if (_document is null) return (0, 0);

        long firstVisible = _scroll?.FirstVisibleLine ?? 0;
        int hOffset = _wordWrap ? 0 : (_scroll?.HorizontalScrollOffset ?? 0);

        int lineIndex = _lineHeight > 0 ? y / _lineHeight : 0;
        long visibleLine = firstVisible + lineIndex;

        long docLine;
        int wrapRowInLine = 0;

        if (_wordWrap)
        {
            var (dl, wro) = WrapRowToDocumentLine(visibleLine);
            docLine = dl;
            wrapRowInLine = wro;
        }
        else if (_folding is not null)
            docLine = _folding.VisibleLineToDocumentLine(visibleLine);
        else
            docLine = visibleLine;

        docLine = Math.Clamp(docLine, 0, _document.LineCount - 1);

        int pixelX = Math.Max(0, x - TextLeftPadding);

        if (_wordWrap)
        {
            // Column (box) selection stores *absolute* expanded columns measured
            // from the start of the document line.  The pixel position gives us
            // the column within the current wrap row; add the wrap-row offset so
            // the result is absolute.
            int expandedCol = (pixelX + _charWidth / 2) / _charWidth;
            expandedCol += wrapRowInLine * WrapColumns;
            return (docLine, expandedCol);
        }

        string lineText = _document.GetLine(docLine);
        string expanded = ExpandTabs(lineText);

        // Compute visual column using a uniform _charWidth grid.
        // This ensures the same pixel position gives the same visual column
        // on every line, making the column selection perfectly rectangular.
        int scrollVisCol = (hOffset <= 0) ? 0
            : CharIndexToVisualColumn(expanded, Math.Min(hOffset, expanded.Length));
        int expandedColNoWrap = scrollVisCol + (pixelX + _charWidth / 2) / _charWidth;

        return (docLine, expandedColNoWrap);
    }

    /// <summary>
    /// Converts a pixel coordinate to a document character offset.
    /// </summary>
    public long HitTestOffset(int x, int y)
    {
        if (_document is null) return 0;

        var (line, col) = HitTestLineColumn(x, y);
        return _document.LineColumnToOffset(line, col);
    }

    /// <summary>
    /// Converts an expanded column position back to a raw character index
    /// in the line, accounting for tab stops.
    /// </summary>
    private int CompressedColumn(string text, int expandedCol)
    {
        int col = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (col >= expandedCol) return i;

            if (text[i] == '\t')
                col += _tabSize - (col % _tabSize);
            else
                col++;
        }

        return text.Length;
    }

    /// <summary>
    /// Returns the pixel x-coordinate for a character position in the
    /// tab-expanded string, accounting for fullwidth (CJK) characters.
    /// <paramref name="from"/> and <paramref name="to"/> are character
    /// indices into <paramref name="expanded"/>.
    /// </summary>
    private int DisplayX(string expanded, int from, int to)
    {
        int px = 0;
        int end = Math.Min(to, expanded.Length);
        for (int i = Math.Max(0, from); i < end; i++)
        {
            int w = GetCharDisplayWidth(expanded, i);
            if (w == 0) continue; // low surrogate — already counted
            px += CharPixelWidth(expanded, i, w);
        }
        return px;
    }

    /// <summary>
    /// Converts a pixel offset (relative to the start of visible text) to a
    /// character index in the tab-expanded string. Walks from <paramref name="startChar"/>
    /// summing character display widths until the pixel position is reached.
    /// </summary>
    private int CharIndexFromPixel(string expanded, int startChar, int pixelX)
    {
        int accumulated = 0;
        for (int i = startChar; i < expanded.Length; i++)
        {
            int w = GetCharDisplayWidth(expanded, i);
            if (w == 0) continue; // low surrogate — already counted
            int charPx = CharPixelWidth(expanded, i, w);
            if (accumulated + charPx / 2 > pixelX)
                return i;
            accumulated += charPx;
        }
        return expanded.Length;
    }

    /// <summary>
    /// Converts a character index in tab-expanded text to a visual column count,
    /// where narrow chars = 1, wide (CJK/emoji) = 2, zero-width = 0.
    /// </summary>
    internal static int CharIndexToVisualColumn(string expanded, int charIndex)
    {
        int vc = 0;
        int end = Math.Min(charIndex, expanded.Length);
        for (int i = 0; i < end; i++)
            vc += GetCharDisplayWidth(expanded, i);
        return vc;
    }

    /// <summary>
    /// Converts a visual column count back to a character index in tab-expanded text.
    /// </summary>
    internal static int VisualColumnToCharIndex(string expanded, int visualCol)
    {
        int vc = 0;
        for (int i = 0; i < expanded.Length; i++)
        {
            if (char.IsLowSurrogate(expanded[i])) continue;
            if (vc >= visualCol) return i;
            vc += GetCharDisplayWidth(expanded, i);
        }
        return expanded.Length;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Emoji-aware text rendering
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the string contains emoji or zero-width characters
    /// that require per-character rendering for correct cursor alignment.
    /// </summary>
    /// <summary>
    /// Returns true if <paramref name="text"/> contains any character that
    /// requires per-character rendering: wide CJK/emoji characters, zero-width
    /// combining/modifier chars, or supplementary-plane characters. GDI's batch
    /// TextRenderer.DrawText uses its own internal advance widths which drift
    /// from our per-character calculations on mixed-width lines.
    /// </summary>
    private static bool ContainsWideOrSpecialChars(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < 0x0300) continue; // ASCII / Latin fast path

            // Emoji-related zero-width characters.
            if (c == '\u200D' || c == '\uFE0F' || c == '\uFE0E' ||
                c == '\u200B' || c == '\u200C' || c == '\u2060' ||
                c == '\u20E3') return true;

            // Any wide character (CJK, emoji, supplementary) → per-char rendering.
            if (GetCharDisplayWidth(text, i) >= 2) return true;
        }
        return false;
    }

    /// <summary>
    /// Renders text with per-character positioning when emoji are present,
    /// so each character is placed at exactly its calculated pixel offset.
    /// Falls back to standard <see cref="TextRenderer.DrawText"/> for
    /// pure ASCII/CJK text where GDI positioning is consistent.
    /// </summary>
    private void DrawTextAligned(Graphics g, string text, Font font, int x, int y, Color color)
    {
        if (!ContainsWideOrSpecialChars(text))
        {
            TextRenderer.DrawText(g, text, font, new Point(x, y), color, DrawFlags);
            return;
        }

        int px = x;
        for (int i = 0; i < text.Length; i++)
        {
            int w = GetCharDisplayWidth(text, i);
            if (w == 0) continue;
            int charPx = CharPixelWidth(text, i, w);
            int len = (char.IsHighSurrogate(text[i]) && i + 1 < text.Length &&
                       char.IsLowSurrogate(text[i + 1])) ? 2 : 1;

            // Collect following zero-width characters (skin tone modifiers,
            // ZWJ, variation selectors) into the same render unit so GDI
            // can compose them into a single glyph (e.g. 👍 + 🏽 → 👍🏽).
            while (i + len < text.Length)
            {
                int nextW = GetCharDisplayWidth(text, i + len);
                if (nextW != 0) break;
                if (char.IsHighSurrogate(text[i + len]) && i + len + 1 < text.Length &&
                    char.IsLowSurrogate(text[i + len + 1]))
                    len += 2; // supplementary zero-width char (e.g. skin tone modifier)
                else
                    len += 1; // BMP zero-width char (e.g. ZWJ, VS16)
            }

            // Keycap sequences (digit + FE0F + 20E3) need the Segoe UI Emoji font
            // because GDI's per-char font fallback can't compose the combining mark.
            Font renderFont = (IsKeycapBase(text[i]) && HasKeycapSuffix(text, i + 1) && _emojiFallbackFont is not null)
                ? _emojiFallbackFont : font;
            TextRenderer.DrawText(g, text.Substring(i, len), renderFont, new Point(px, y), color, DrawFlags);
            px += charPx;
        }
    }

    /// <summary>
    /// Returns the pixel width for a character at <paramref name="index"/> in
    /// <paramref name="text"/>, using the correct measured width for narrow,
    /// BMP CJK, or supplementary-plane CJK characters.
    /// </summary>
    private int CharPixelWidth(string text, int index, int displayWidth)
    {
        if (displayWidth <= 1) return _charWidth;

        char c = text[index];
        if (char.IsHighSurrogate(c))
        {
            // Supplementary plane: distinguish CJK vs emoji for correct fallback font width.
            if (index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                int cp = char.ConvertToUtf32(c, text[index + 1]);
                if (cp >= 0x1F1E0 && cp <= 0x1FAFF)
                    return _suppEmojiCharWidth;
            }
            return _suppCjkCharWidth;
        }

        // BMP emoji uses separately measured width (also keycap sequences).
        if (IsDefaultEmojiPresentation(c) || IsKeycapBase(c))
            return _bmpEmojiCharWidth;

        return _cjkCharWidth;
    }

    /// <summary>
    /// Returns the display width of a character in monospace cells.
    /// East Asian fullwidth and wide characters occupy 2 cells;
    /// all others occupy 1 cell.
    /// </summary>
    internal static int GetCharDisplayWidth(char c)
    {
        // Fast path: ASCII + Latin-1 Supplement + Latin Extended-A/B.
        if (c < 0x0300) return 1;

        // Combining diacritical marks — zero width (accents, Zalgo text, etc.)
        if (c <= 0x036F) return 0;                               // U+0300–U+036F Combining Diacritical Marks
        if (c >= 0x0483 && c <= 0x0489) return 0;                // Combining Cyrillic

        // Rest of BMP below CJK/wide ranges.
        if (c < 0x1100) return 1;

        // Low surrogate of a pair — caller should use the string overload instead.
        // Return 0 so it doesn't add extra width when encountered alone.
        if (char.IsLowSurrogate(c)) return 0;

        // Zero-width characters: ZWJ, variation selectors, etc.
        if (c == '\u200B' || c == '\u200C' || c == '\u200D' ||  // ZWS, ZWNJ, ZWJ
            c == '\uFE0E' || c == '\uFE0F' ||                   // variation selectors
            c == '\u2060' || c == '\uFEFF' ||                    // word joiner, BOM
            c == '\u20E3')                                       // combining enclosing keycap
            return 0;

        // Combining mark ranges above U+1100.
        if (c >= 0x1AB0 && c <= 0x1AFF) return 0;               // Combining Diacritical Marks Extended
        if (c >= 0x1DC0 && c <= 0x1DFF) return 0;               // Combining Diacritical Marks Supplement
        if (c >= 0x20D0 && c <= 0x20FF) return 0;               // Combining Diacritical Marks for Symbols
        if (c >= 0xFE20 && c <= 0xFE2F) return 0;               // Combining Half Marks

        // East Asian Fullwidth and Wide character ranges.
        // Based on Unicode East Asian Width property (UAX #11).
        if (c >= 0x1100 && c <= 0x115F) return 2; // Hangul Jamo
        if (c >= 0x2E80 && c <= 0x303E) return 2; // CJK Radicals, Kangxi, Ideographic, CJK Symbols
        if (c >= 0x3041 && c <= 0x33BF) return 2; // Hiragana, Katakana, Bopomofo, Hangul Compat Jamo, Kanbun, CJK Strokes
        if (c >= 0x3400 && c <= 0x4DBF) return 2; // CJK Unified Ideographs Extension A
        if (c >= 0x4E00 && c <= 0x9FFF) return 2; // CJK Unified Ideographs
        if (c >= 0xA000 && c <= 0xA4CF) return 2; // Yi Syllables and Radicals
        if (c >= 0xAC00 && c <= 0xD7AF) return 2; // Hangul Syllables
        if (c >= 0xF900 && c <= 0xFAFF) return 2; // CJK Compatibility Ideographs
        if (c >= 0xFE30 && c <= 0xFE6F) return 2; // CJK Compatibility Forms, Small Form Variants
        if (c >= 0xFF01 && c <= 0xFF60) return 2; // Fullwidth Latin, Halfwidth CJK punctuation
        if (c >= 0xFFE0 && c <= 0xFFE6) return 2; // Fullwidth signs (¢, £, ¥, etc.)

        // BMP characters with default emoji presentation (Emoji_Presentation=Yes).
        // GDI renders these via Segoe UI Symbol at wider-than-_charWidth advance.
        if (IsDefaultEmojiPresentation(c)) return 2;

        // High surrogate — can't determine width without the low surrogate.
        // Callers iterating strings should use GetCharDisplayWidth(string, int).
        if (char.IsHighSurrogate(c)) return 2; // Assume wide (most supplementary CJK/emoji are)

        return 1;
    }

    /// <summary>
    /// Returns the display width of the character at <paramref name="index"/>
    /// in <paramref name="text"/>, correctly handling surrogate pairs for
    /// supplementary-plane characters (e.g. CJK Extension B/C/D/E/F/G/H).
    /// </summary>
    internal static int GetCharDisplayWidth(string text, int index)
    {
        char c = text[index];

        // Low surrogate — already counted with its high surrogate.
        if (char.IsLowSurrogate(c)) return 0;

        if (!char.IsHighSurrogate(c))
        {
            // Keycap sequence: digit/# /* + (optional FE0F) + U+20E3 → wide emoji.
            if (IsKeycapBase(c) && HasKeycapSuffix(text, index + 1))
                return 2;
            return GetCharDisplayWidth(c);
        }

        // Surrogate pair → decode full code point.
        if (index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            int cp = char.ConvertToUtf32(c, text[index + 1]);

            // Second Regional Indicator in a flag pair → zero width.
            if (cp >= 0x1F1E0 && cp <= 0x1F1FF && IsSecondRegionalIndicator(text, index))
                return 0;

            return GetCodePointDisplayWidth(cp);
        }

        // Unpaired high surrogate — treat as narrow.
        return 1;
    }

    /// <summary>
    /// Returns the display width for a full Unicode code point (including supplementary planes).
    /// </summary>
    private static int GetCodePointDisplayWidth(int codePoint)
    {
        // BMP range — delegate to the char overload.
        if (codePoint <= 0xFFFF)
            return GetCharDisplayWidth((char)codePoint);

        // Skin tone modifiers — zero-width, they modify the preceding emoji.
        if (codePoint >= 0x1F3FB && codePoint <= 0x1F3FF) return 0;

        // Emoji ranges in supplementary planes.
        if (codePoint >= 0x1F1E0 && codePoint <= 0x1F1FF) return 2; // Regional Indicator Symbols (flags)
        if (codePoint >= 0x1F300 && codePoint <= 0x1F5FF) return 2; // Misc Symbols and Pictographs
        if (codePoint >= 0x1F600 && codePoint <= 0x1F64F) return 2; // Emoticons
        if (codePoint >= 0x1F680 && codePoint <= 0x1F6FF) return 2; // Transport and Map Symbols
        if (codePoint >= 0x1F900 && codePoint <= 0x1F9FF) return 2; // Supplemental Symbols and Pictographs
        if (codePoint >= 0x1FA70 && codePoint <= 0x1FAFF) return 2; // Symbols and Pictographs Extended-A

        // Supplementary CJK / East Asian wide ranges (UAX #11).
        if (codePoint >= 0x20000 && codePoint <= 0x2A6DF) return 2; // CJK Unified Ideographs Extension B
        if (codePoint >= 0x2A700 && codePoint <= 0x2B73F) return 2; // Extension C
        if (codePoint >= 0x2B740 && codePoint <= 0x2B81F) return 2; // Extension D
        if (codePoint >= 0x2B820 && codePoint <= 0x2CEAF) return 2; // Extension E
        if (codePoint >= 0x2CEB0 && codePoint <= 0x2EBEF) return 2; // Extension F
        if (codePoint >= 0x2EBF0 && codePoint <= 0x2F7FF) return 2; // Extension I
        if (codePoint >= 0x2F800 && codePoint <= 0x2FA1F) return 2; // CJK Compat Ideographs Supplement
        if (codePoint >= 0x30000 && codePoint <= 0x3134F) return 2; // Extension G
        if (codePoint >= 0x31350 && codePoint <= 0x323AF) return 2; // Extension H

        return 1;
    }

    /// <summary>
    /// Returns true if the BMP character has default emoji presentation
    /// (Emoji_Presentation=Yes in Unicode), meaning it renders as a wide
    /// emoji glyph even without a trailing U+FE0F variation selector.
    /// </summary>
    private static bool IsDefaultEmojiPresentation(char c)
    {
        return c switch
        {
            '\u231A' or '\u231B' => true,
            >= '\u23E9' and <= '\u23F3' => true,
            >= '\u23F8' and <= '\u23FA' => true,
            >= '\u25FD' and <= '\u25FE' => true,
            >= '\u2614' and <= '\u2615' => true,
            >= '\u2648' and <= '\u2653' => true,
            '\u267F' or '\u2693' or '\u26A1' => true,
            >= '\u26AA' and <= '\u26AB' => true,
            >= '\u26BD' and <= '\u26BE' => true,
            >= '\u26C4' and <= '\u26C5' => true,
            '\u26CE' or '\u26D4' or '\u26EA' => true,
            >= '\u26F2' and <= '\u26F3' => true,
            '\u26F5' or '\u26FA' or '\u26FD' => true,
            '\u2702' or '\u2705' => true,
            >= '\u2708' and <= '\u270D' => true,
            '\u270F' or '\u2712' or '\u2714' or '\u2716' => true,
            '\u271D' or '\u2721' or '\u2728' => true,
            >= '\u2733' and <= '\u2734' => true,
            '\u2744' or '\u2747' or '\u274C' or '\u274E' => true,
            >= '\u2753' and <= '\u2755' => true,
            '\u2757' => true,
            >= '\u2763' and <= '\u2764' => true,
            >= '\u2795' and <= '\u2797' => true,
            '\u27A1' or '\u27B0' or '\u27BF' => true,
            >= '\u2934' and <= '\u2935' => true,
            >= '\u2B05' and <= '\u2B07' => true,
            >= '\u2B1B' and <= '\u2B1C' => true,
            '\u2B50' or '\u2B55' => true,
            '\u3030' or '\u303D' or '\u3297' or '\u3299' => true,
            _ => false,
        };
    }

    /// <summary>
    /// Returns true if the Regional Indicator surrogate pair at <paramref name="index"/>
    /// is the second in a flag pair (e.g. 🇧 in 🇬🇧), by counting consecutive
    /// preceding Regional Indicators.
    /// </summary>
    private static bool IsSecondRegionalIndicator(string text, int index)
    {
        int riCount = 0;
        int j = index;
        while (j >= 2)
        {
            j -= 2;
            if (!char.IsHighSurrogate(text[j]) || !char.IsLowSurrogate(text[j + 1])) break;
            int prevCp = char.ConvertToUtf32(text[j], text[j + 1]);
            if (prevCp < 0x1F1E0 || prevCp > 0x1F1FF) break;
            riCount++;
        }
        return riCount % 2 == 1;
    }

    /// <summary>
    /// Returns true if <paramref name="c"/> is a valid keycap base character
    /// (0-9, #, *) that can form a keycap emoji sequence with U+FE0F + U+20E3.
    /// </summary>
    private static bool IsKeycapBase(char c) =>
        (c >= '0' && c <= '9') || c == '#' || c == '*';

    /// <summary>
    /// Returns true if the text starting at <paramref name="i"/> contains
    /// an optional U+FE0F followed by U+20E3 (combining enclosing keycap).
    /// </summary>
    private static bool HasKeycapSuffix(string text, int i)
    {
        if (i >= text.Length) return false;
        if (text[i] == '\uFE0F') i++;
        return i < text.Length && text[i] == '\u20E3';
    }

    // ────────────────────────────────────────────────────────────────────
    //  Cleanup
    // ────────────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editorFont.Dispose();
            _emojiFallbackFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
