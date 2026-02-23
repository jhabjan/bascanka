using System.Drawing;
using Bascanka.Core.Buffer;
using Bascanka.Core.Diff;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Controls;

/// <summary>
/// Renders the line-number gutter to the left of the editor surface.
/// Supports line numbers, bookmark indicators, fold expand/collapse
/// buttons, and line/multi-line selection via mouse interaction.
/// </summary>
public sealed class GutterRenderer
{
    private static int GutterPaddingLeft => EditorControl.DefaultGutterPaddingLeft;
    private static int GutterPaddingRight => EditorControl.DefaultGutterPaddingRight;
    private static int BookmarkDiameter => EditorControl.DefaultBookmarkSize;
    private static int FoldButtonSize => EditorControl.DefaultFoldButtonSize;

    private readonly HashSet<long> _bookmarkedLines = new();
    private long _widthCacheTotalLines = -1;
    private string _widthCacheFontKey = string.Empty;

    // ────────────────────────────────────────────────────────────────────
    //  Properties
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The computed width of the gutter in pixels. Recalculated when the
    /// document line count changes.
    /// </summary>
    public int Width { get; private set; } = 50;

    /// <summary>Background colour for the gutter area.</summary>
    public Color BackgroundColor { get; set; } = Color.FromArgb(30, 30, 30);

    /// <summary>Foreground colour for line number text.</summary>
    public Color TextColor { get; set; } = Color.FromArgb(133, 133, 133);

    /// <summary>Foreground colour for the current line number.</summary>
    public Color CurrentLineColor { get; set; } = Color.FromArgb(198, 198, 198);

    /// <summary>Colour of bookmark indicator circles.</summary>
    public Color BookmarkColor { get; set; } = Color.FromArgb(64, 128, 255);

    /// <summary>Per-line diff metadata. When set, line numbers are remapped and gutter bars drawn.</summary>
    public DiffLine[]? DiffLineMarkers { get; set; }

    // ────────────────────────────────────────────────────────────────────
    //  Width calculation
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recalculates the gutter width based on the maximum line number.
    /// </summary>
    public void UpdateWidth(long totalLines, Font font, Graphics g)
    {
        string fontKey = $"{font.FontFamily.Name}|{font.SizeInPoints}|{font.Style}";
        if (_widthCacheTotalLines == totalLines && string.Equals(_widthCacheFontKey, fontKey, StringComparison.Ordinal))
            return;

        int digits = Math.Max(2, totalLines.ToString().Length);
        string sample = new string('9', digits);
        Size textSize = TextRenderer.MeasureText(g, sample, font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        Width = GutterPaddingLeft + textSize.Width + GutterPaddingRight + FoldButtonSize + 4;
        _widthCacheTotalLines = totalLines;
        _widthCacheFontKey = fontKey;
    }

    /// <summary>
    /// Applies theme colours to the gutter.
    /// </summary>
    public void ApplyTheme(ITheme theme)
    {
        BackgroundColor = theme.GutterBackground;
        TextColor = theme.GutterForeground;
        CurrentLineColor = theme.GutterCurrentLine;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Bookmarks
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Toggles a bookmark on the specified line.</summary>
    public void ToggleBookmark(long line)
    {
        if (!_bookmarkedLines.Remove(line))
            _bookmarkedLines.Add(line);
    }

    /// <summary>Returns whether the specified line has a bookmark.</summary>
    public bool HasBookmark(long line) => _bookmarkedLines.Contains(line);

    /// <summary>Clears all bookmarks.</summary>
    public void ClearBookmarks() => _bookmarkedLines.Clear();

    // ────────────────────────────────────────────────────────────────────
    //  Rendering
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Paints the gutter for the visible lines.
    /// </summary>
    /// <param name="g">The graphics surface.</param>
    /// <param name="font">The monospace font used for line numbers.</param>
    /// <param name="lineHeight">Height of a single line in pixels.</param>
    /// <param name="firstVisibleLine">Zero-based index of the first visible line.</param>
    /// <param name="visibleLineCount">Number of lines visible in the viewport.</param>
    /// <param name="totalLines">Total document line count.</param>
    /// <param name="currentLine">The line the caret is on.</param>
    /// <param name="foldingManager">Optional folding manager for fold buttons.</param>
    /// <param name="theme">The active theme for fold marker colours.</param>
    public void Render(
        Graphics g,
        Font font,
        int lineHeight,
        long firstVisibleLine,
        int visibleLineCount,
        long totalLines,
        long currentLine,
        FoldingManager? foldingManager,
        ITheme? theme,
        Func<long, int>? wrapRowCount = null,
        Func<long, (long DocLine, int WrapOffset)>? wrapRowToDocLine = null)
    {
        // Fill gutter background.
        using var bgBrush = new SolidBrush(BackgroundColor);
        g.FillRectangle(bgBrush, 0, 0, Width, g.ClipBounds.Height);

        // Draw separator line.
        using var separatorPen = new Pen(Color.FromArgb(50, 128, 128, 128));
        g.DrawLine(separatorPen, Width - 1, 0, Width - 1, g.ClipBounds.Height);

        // Pre-create reusable GDI objects.
        using var bookmarkBrush = new SolidBrush(BookmarkColor);
        Color foldColor = theme?.FoldingMarker ?? Color.Gray;
        using var foldPen = new Pen(foldColor, 1);

        // Measure a single digit once — monospace means all same-length
        // numbers have the same width, so we only measure per distinct length.
        int lineNumberRight = Width - GutterPaddingRight - FoldButtonSize - 4;
        int lastMeasuredLen = -1;
        int lastMeasuredWidth = 0;
        int lastMeasuredHeight = 0;

        int visualRow = 0;
        int maxVisualRows = visibleLineCount;

        // When word-wrap is active and firstVisibleLine is a wrap-row index,
        // use the mapping function to resolve the starting document line.
        long startDocLine = -1;
        int firstLineWrapOff = 0;
        if (wrapRowToDocLine is not null)
        {
            var (dl, wo) = wrapRowToDocLine(firstVisibleLine);
            startDocLine = dl;
            firstLineWrapOff = wo;
        }

        long iterDocLine = startDocLine >= 0 ? startDocLine : -1;

        for (int i = 0; visualRow < maxVisualRows; i++)
        {
            long docLine;
            if (iterDocLine >= 0)
            {
                // Word-wrap mode: iterate document lines forward.
                docLine = iterDocLine;
                // Advance to next visible document line for next iteration.
                iterDocLine++;
                while (iterDocLine < totalLines && foldingManager is not null && !foldingManager.IsLineVisible(iterDocLine))
                    iterDocLine++;
            }
            else
            {
                docLine = foldingManager is not null
                    ? foldingManager.VisibleLineToDocumentLine(firstVisibleLine + i)
                    : firstVisibleLine + i;
            }

            if (docLine >= totalLines) break;

            int rowsForLine = wrapRowCount?.Invoke(docLine) ?? 1;
            // For the first line, skip the wrap rows before the offset.
            int firstRowOffset = (i == 0 && startDocLine >= 0) ? firstLineWrapOff : 0;
            int renderedRows = rowsForLine - firstRowOffset;
            int y = visualRow * lineHeight;
            bool isCurrent = docLine == currentLine;

            // Line number text — only on the first visual row for this doc line.
            // In diff mode, use original line numbers and skip padding lines.
            bool skipLineNumber = false;
            string lineNum;
            if (DiffLineMarkers is not null && docLine < DiffLineMarkers.Length)
            {
                var marker = DiffLineMarkers[docLine];
                if (marker.OriginalLineNumber == -1)
                {
                    skipLineNumber = true;
                    lineNum = string.Empty;
                }
                else
                {
                    lineNum = (marker.OriginalLineNumber + 1).ToString();
                }

                // Draw coloured gutter bar for diff lines.
                Color? barColor = marker.Type switch
                {
                    DiffLineType.Added    => Color.FromArgb(200, 60, 190, 220),
                    DiffLineType.Removed  => Color.FromArgb(200, 220, 70, 160),
                    DiffLineType.Modified => Color.FromArgb(200, 170, 100, 245),
                    _ => null,
                };
                if (barColor.HasValue)
                {
                    using var barBrush = new SolidBrush(barColor.Value);
                    g.FillRectangle(barBrush, 0, y, 3, lineHeight);
                }
            }
            else
            {
                lineNum = (docLine + 1).ToString();
            }

            Color textColor = isCurrent ? CurrentLineColor : TextColor;

            if (!skipLineNumber)
            {
                // Cache MeasureText by string length (all n-digit numbers are same width in monospace).
                if (lineNum.Length != lastMeasuredLen)
                {
                    Size textSize = TextRenderer.MeasureText(g, lineNum, font, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    lastMeasuredWidth = textSize.Width;
                    lastMeasuredHeight = textSize.Height;
                    lastMeasuredLen = lineNum.Length;
                }

                int textX = lineNumberRight - lastMeasuredWidth;
                int textY = y + (lineHeight - lastMeasuredHeight) / 2;

                TextRenderer.DrawText(g, lineNum, font,
                    new Point(textX, textY), textColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            // Bookmark indicator (blue circle).
            if (_bookmarkedLines.Contains(docLine))
            {
                int bx = GutterPaddingLeft / 2;
                int by = y + (lineHeight - BookmarkDiameter) / 2;
                g.FillEllipse(bookmarkBrush, bx, by, BookmarkDiameter, BookmarkDiameter);
            }

            // Fold expand/collapse button.
            if (foldingManager is not null)
            {
                bool isFoldStart = foldingManager.IsFoldStart(docLine);
                if (isFoldStart)
                {
                    bool isCollapsed = foldingManager.IsCollapsed(docLine);
                    int fx = Width - FoldButtonSize - 2;
                    int fy = y + (lineHeight - FoldButtonSize) / 2;

                    // Draw box.
                    g.DrawRectangle(foldPen, fx, fy, FoldButtonSize, FoldButtonSize);

                    // Draw minus sign (always present).
                    int midY = fy + FoldButtonSize / 2;
                    g.DrawLine(foldPen, fx + 2, midY, fx + FoldButtonSize - 2, midY);

                    if (isCollapsed)
                    {
                        // Draw vertical bar to make a plus sign.
                        int midX = fx + FoldButtonSize / 2;
                        g.DrawLine(foldPen, midX, fy + 2, midX, fy + FoldButtonSize - 2);
                    }
                }
            }

            visualRow += renderedRows;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Mouse hit-testing
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Determines whether a click at the given X position is on a fold button.
    /// </summary>
    public bool IsFoldButtonHit(int x) =>
        x >= Width - FoldButtonSize - 4 && x <= Width;

    /// <summary>
    /// Returns the document line index for a Y coordinate in the gutter.
    /// </summary>
    public long GetLineFromY(int y, int lineHeight, long firstVisibleLine,
        FoldingManager? foldingManager, Func<long, int>? wrapRowCount = null,
        Func<long, (long DocLine, int WrapOffset)>? wrapRowToDocLine = null)
    {
        int targetRow = lineHeight > 0 ? y / lineHeight : 0;

        if (wrapRowCount is not null && wrapRowToDocLine is not null)
        {
            var (startDocLine, wrapOff) = wrapRowToDocLine(firstVisibleLine);
            int visualRow = 0;
            long totalLines = foldingManager is not null
                ? long.MaxValue // will be bounded by iteration
                : long.MaxValue;

            for (long docLine = startDocLine; ; docLine++)
            {
                if (foldingManager is not null && !foldingManager.IsLineVisible(docLine))
                    continue;

                int rows = wrapRowCount(docLine);
                int firstRowOff = (docLine == startDocLine) ? wrapOff : 0;
                int rendered = rows - firstRowOff;

                if (targetRow < visualRow + rendered)
                    return docLine;

                visualRow += rendered;
            }
        }
        else if (wrapRowCount is not null)
        {
            int visualRow = 0;
            for (long i = 0; ; i++)
            {
                long docLine = foldingManager is not null
                    ? foldingManager.VisibleLineToDocumentLine(firstVisibleLine + i)
                    : firstVisibleLine + i;

                int rows = wrapRowCount(docLine);
                if (targetRow < visualRow + rows)
                    return docLine;

                visualRow += rows;
            }
        }

        int visibleIndex = targetRow;
        long visLine = firstVisibleLine + visibleIndex;

        if (foldingManager is not null)
            return foldingManager.VisibleLineToDocumentLine(visLine);

        return visLine;
    }
}
