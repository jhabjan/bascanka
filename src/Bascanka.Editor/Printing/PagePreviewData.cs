using Bascanka.Core.Syntax;

namespace Bascanka.Editor.Printing;

/// <summary>
/// Data describing a single page preview for print-preview UIs.
/// </summary>
public sealed class PagePreviewData
{
    /// <summary>One-based page number.</summary>
    public int PageNumber { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages { get; init; }

    /// <summary>The lines of text on this page.</summary>
    public List<string> Lines { get; init; } = [];

    /// <summary>The tokens for each line (for syntax colouring in the preview).</summary>
    public List<List<Token>> LineTokens { get; init; } = [];
}
