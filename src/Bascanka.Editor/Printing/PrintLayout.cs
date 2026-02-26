namespace Bascanka.Editor.Printing;

/// <summary>
/// Describes the computed layout for a printed page, including the number
/// of characters and lines that fit and the bounding rectangles for the
/// header, body, and footer areas.
/// </summary>
public sealed class PrintLayout
{
    /// <summary>Maximum number of characters that fit on a single printed line.</summary>
    public int CharsPerLine { get; init; }

    /// <summary>Maximum number of lines that fit in the body area of a page.</summary>
    public int LinesPerPage { get; init; }

    /// <summary>The printable body area in page units.</summary>
    public RectangleF PrintableArea { get; init; }

    /// <summary>The header area rectangle (above the body).</summary>
    public RectangleF HeaderArea { get; init; }

    /// <summary>The footer area rectangle (below the body).</summary>
    public RectangleF FooterArea { get; init; }

    /// <summary>The measured height of a single line of text.</summary>
    public float LineHeight { get; init; }

    /// <summary>The measured width of a single monospaced character.</summary>
    public float CharWidth { get; init; }
}
