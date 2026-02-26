using System.Drawing;
using System.Drawing.Printing;

namespace Bascanka.Editor.Printing;

/// <summary>
/// Calculates page layout parameters (characters per line, lines per page,
/// area rectangles) for a given set of <see cref="PageSettings"/> and
/// <see cref="Font"/>.  Supports optional word-wrap and provides page-break
/// calculation for long documents.
/// </summary>
public sealed class PrintLayoutEngine
{
    // ── Constants ───────────────────────────────────────────────────────

    /// <summary>Height reserved for the header (in hundredths of an inch).</summary>
    private const float HeaderHeight = 30f;

    /// <summary>Height reserved for the footer (in hundredths of an inch).</summary>
    private const float FooterHeight = 30f;

    /// <summary>Vertical gap between header/footer and the body area.</summary>
    private const float HeaderFooterGap = 10f;

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Computes the layout for a single page based on the given print
    /// settings and font.
    /// </summary>
    /// <param name="settings">
    /// The <see cref="PageSettings"/> that specify margins, paper size, and
    /// orientation.
    /// </param>
    /// <param name="font">The monospaced font used for printing.</param>
    /// <returns>A fully populated <see cref="PrintLayout"/>.</returns>
    public static PrintLayout CalculateLayout(PageSettings settings, Font font)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(font);

        // Determine the printable area from the page settings.
        RectangleF bounds = settings.PrintableArea;
        float marginLeft = settings.Margins.Left;
        float marginTop = settings.Margins.Top;
        float marginRight = settings.Margins.Right;
        float marginBottom = settings.Margins.Bottom;

        float availableWidth = bounds.Width - marginLeft - marginRight;
        float availableHeight = bounds.Height - marginTop - marginBottom;

        if (availableWidth < 1f) availableWidth = 1f;
        if (availableHeight < 1f) availableHeight = 1f;

        float originX = bounds.Left + marginLeft;
        float originY = bounds.Top + marginTop;

        // Measure character and line metrics.
        float lineHeight;
        float charWidth;
        using (Bitmap bmp = new(1, 1))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            lineHeight = font.GetHeight(g);
            SizeF charSize = g.MeasureString("W", font, 0, StringFormat.GenericTypographic);
            charWidth = charSize.Width;
        }

        if (lineHeight < 1f) lineHeight = 12f;
        if (charWidth < 1f) charWidth = 7f;

        // Header area
        RectangleF headerArea = new(originX, originY, availableWidth, HeaderHeight);

        // Footer area
        float footerY = originY + availableHeight - FooterHeight;
        RectangleF footerArea = new(originX, footerY, availableWidth, FooterHeight);

        // Body area (between header and footer, with gaps)
        float bodyTop = originY + HeaderHeight + HeaderFooterGap;
        float bodyBottom = footerY - HeaderFooterGap;
        float bodyHeight = bodyBottom - bodyTop;
        if (bodyHeight < lineHeight) bodyHeight = lineHeight;

        RectangleF printableArea = new(originX, bodyTop, availableWidth, bodyHeight);

        // Characters and lines
        int charsPerLine = Math.Max(1, (int)(availableWidth / charWidth));
        int linesPerPage = Math.Max(1, (int)(bodyHeight / lineHeight));

        return new PrintLayout
        {
            CharsPerLine = charsPerLine,
            LinesPerPage = linesPerPage,
            PrintableArea = printableArea,
            HeaderArea = headerArea,
            FooterArea = footerArea,
            LineHeight = lineHeight,
            CharWidth = charWidth,
        };
    }

    /// <summary>
    /// Breaks a document into pages, returning the line ranges for each page.
    /// </summary>
    /// <param name="totalLines">Total number of lines in the document.</param>
    /// <param name="linesPerPage">Lines per page from the computed layout.</param>
    /// <param name="wordWrap">
    /// If <see langword="true"/>, lines that exceed the available width are
    /// wrapped and consume additional visual lines.
    /// </param>
    /// <param name="lineLengths">
    /// An array of line lengths (in characters) for word-wrap calculation.
    /// Ignored when <paramref name="wordWrap"/> is <see langword="false"/>.
    /// </param>
    /// <param name="charsPerLine">Characters per line from the layout.</param>
    /// <returns>
    /// A list of <see cref="PageRange"/> values describing which document
    /// lines appear on each page.
    /// </returns>
    public static List<PageRange> CalculatePageBreaks(
        int totalLines,
        int linesPerPage,
        bool wordWrap,
        int[]? lineLengths,
        int charsPerLine)
    {
        var pages = new List<PageRange>();
        if (totalLines == 0)
        {
            pages.Add(new PageRange(0, 0));
            return pages;
        }

        if (!wordWrap || lineLengths is null)
        {
            // Simple pagination: each page holds exactly linesPerPage lines.
            for (int startLine = 0; startLine < totalLines; startLine += linesPerPage)
            {
                int endLine = Math.Min(startLine + linesPerPage - 1, totalLines - 1);
                pages.Add(new PageRange(startLine, endLine));
            }
        }
        else
        {
            // Word-wrap aware pagination: count visual lines consumed.
            int pageStartLine = 0;
            int visualLinesUsed = 0;

            for (int line = 0; line < totalLines; line++)
            {
                int lineLen = line < lineLengths.Length ? lineLengths[line] : 0;
                int visualLines = lineLen <= charsPerLine ? 1 :
                    (lineLen + charsPerLine - 1) / charsPerLine;

                if (visualLinesUsed + visualLines > linesPerPage && visualLinesUsed > 0)
                {
                    // Finish the current page.
                    pages.Add(new PageRange(pageStartLine, line - 1));
                    pageStartLine = line;
                    visualLinesUsed = 0;
                }

                visualLinesUsed += visualLines;
            }

            // Last page.
            if (pageStartLine < totalLines)
                pages.Add(new PageRange(pageStartLine, totalLines - 1));
        }

        return pages;
    }
}

/// <summary>
/// Describes a contiguous range of document lines that fit on a single
/// printed page.
/// </summary>
/// <param name="StartLine">Zero-based index of the first line on the page.</param>
/// <param name="EndLine">Zero-based index of the last line on the page (inclusive).</param>
public readonly record struct PageRange(int StartLine, int EndLine)
{
    /// <summary>Number of document lines on this page.</summary>
    public int LineCount => EndLine - StartLine + 1;
}
