using Bascanka.Core.Buffer;

namespace Bascanka.Editor.Controls;

/// <summary>
/// Represents a foldable region defined by a start line and end line.
/// </summary>
public readonly record struct FoldRegion(long StartLine, long EndLine);

/// <summary>
/// Manages code-folding regions, collapsed state, and the mapping between
/// visible (display) lines and document lines.  Supports both brace-based
/// and indent-based folding detection.
/// </summary>
public sealed class FoldingManager
{
    private readonly List<FoldRegion> _regions = new();
    private readonly HashSet<long> _collapsedStartLines = new();

    // Pre-computed lookup for fast fold-start checking.
    private readonly Dictionary<long, FoldRegion> _regionByStartLine = new();

    /// <summary>Raised when folding state changes (regions added/removed, toggled).</summary>
    public event Action? FoldingChanged;

    // ────────────────────────────────────────────────────────────────────
    //  Properties
    // ────────────────────────────────────────────────────────────────────

    /// <summary>All detected foldable regions.</summary>
    public IReadOnlyList<FoldRegion> Regions => _regions;

    /// <summary>The set of start lines that are currently collapsed.</summary>
    public IReadOnlyCollection<long> CollapsedStartLines => _collapsedStartLines;

    // ────────────────────────────────────────────────────────────────────
    //  Region management
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces all folding regions with the provided list.
    /// Collapsed state is preserved for regions that still exist.
    /// </summary>
    public void SetRegions(IEnumerable<FoldRegion> regions)
    {
        _regions.Clear();
        _regionByStartLine.Clear();

        foreach (var r in regions)
        {
            if (r.EndLine > r.StartLine)
            {
                _regions.Add(r);
                _regionByStartLine[r.StartLine] = r;
            }
        }

        // Remove collapsed entries for regions that no longer exist.
        _collapsedStartLines.IntersectWith(_regionByStartLine.Keys);

        FoldingChanged?.Invoke();
    }

    /// <summary>Returns whether the given line is the start of a foldable region.</summary>
    public bool IsFoldStart(long line) => _regionByStartLine.ContainsKey(line);

    /// <summary>Returns whether the fold region starting at the given line is collapsed.</summary>
    public bool IsCollapsed(long startLine) => _collapsedStartLines.Contains(startLine);

    // ────────────────────────────────────────────────────────────────────
    //  Toggling
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Toggles the collapsed state of the fold region starting at <paramref name="startLine"/>.
    /// </summary>
    public void Toggle(long startLine)
    {
        if (!_regionByStartLine.ContainsKey(startLine)) return;

        if (!_collapsedStartLines.Remove(startLine))
            _collapsedStartLines.Add(startLine);

        FoldingChanged?.Invoke();
    }

    /// <summary>Expands all collapsed regions.</summary>
    public void ExpandAll()
    {
        if (_collapsedStartLines.Count == 0) return;
        _collapsedStartLines.Clear();
        FoldingChanged?.Invoke();
    }

    /// <summary>Collapses all foldable regions.</summary>
    public void CollapseAll()
    {
        bool changed = false;
        foreach (var r in _regions)
        {
            if (_collapsedStartLines.Add(r.StartLine))
                changed = true;
        }

        if (changed)
            FoldingChanged?.Invoke();
    }

    /// <summary>Collapses the fold region starting at <paramref name="startLine"/>.</summary>
    public void Collapse(long startLine)
    {
        if (!_regionByStartLine.ContainsKey(startLine)) return;
        if (_collapsedStartLines.Add(startLine))
            FoldingChanged?.Invoke();
    }

    /// <summary>Expands the fold region starting at <paramref name="startLine"/>.</summary>
    public void Expand(long startLine)
    {
        if (_collapsedStartLines.Remove(startLine))
            FoldingChanged?.Invoke();
    }

    /// <summary>
    /// Expands any collapsed region that hides the given line, making it visible.
    /// </summary>
    public void EnsureLineVisible(long line)
    {
        if (_collapsedStartLines.Count == 0) return;

        bool changed = false;
        foreach (long startLine in _collapsedStartLines.ToArray())
        {
            if (_regionByStartLine.TryGetValue(startLine, out FoldRegion region))
            {
                if (line > region.StartLine && line <= region.EndLine)
                {
                    _collapsedStartLines.Remove(startLine);
                    changed = true;
                }
            }
        }

        if (changed)
            FoldingChanged?.Invoke();
    }

    /// <summary>
    /// Given a line that may be inside a collapsed region, returns the nearest
    /// visible line in the given direction.
    /// </summary>
    public long NextVisibleLine(long line, long totalLines, bool forward)
    {
        if (_collapsedStartLines.Count == 0) return line;

        foreach (long startLine in _collapsedStartLines)
        {
            if (_regionByStartLine.TryGetValue(startLine, out FoldRegion region))
            {
                if (line > region.StartLine && line <= region.EndLine)
                {
                    if (forward)
                        return Math.Min(region.EndLine + 1, totalLines - 1);
                    else
                        return region.StartLine;
                }
            }
        }

        return line;
    }

    /// <summary>
    /// Returns the fold region whose StartLine equals <paramref name="line"/>,
    /// or the innermost collapsed region containing that line, or null.
    /// </summary>
    public FoldRegion? GetFoldRegionContaining(long line)
    {
        // Direct match — O(1).
        if (_regionByStartLine.TryGetValue(line, out FoldRegion exact))
            return exact;

        // Search for the innermost region containing this line.
        FoldRegion? best = null;
        foreach (var r in _regions)
        {
            if (line >= r.StartLine && line <= r.EndLine)
            {
                if (best is null || (r.EndLine - r.StartLine) < (best.Value.EndLine - best.Value.StartLine))
                    best = r;
            }
        }

        return best;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Visibility
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="false"/> if the line is hidden inside a
    /// collapsed fold region.  The start line of a collapsed region is
    /// always visible; lines from startLine+1 through endLine are hidden.
    /// </summary>
    public bool IsLineVisible(long line)
    {
        foreach (long startLine in _collapsedStartLines)
        {
            if (_regionByStartLine.TryGetValue(startLine, out FoldRegion region))
            {
                if (line > region.StartLine && line <= region.EndLine)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Converts a visible (display) line index to the actual document line index.
    /// </summary>
    public long VisibleLineToDocumentLine(long visibleLine)
    {
        if (_collapsedStartLines.Count == 0)
            return visibleLine;

        long docLine = 0;
        long visCount = 0;

        // Build a sorted list of collapsed ranges for efficient traversal.
        var collapsed = GetSortedCollapsedRanges();

        int rangeIdx = 0;
        while (visCount <= visibleLine)
        {
            // Check if docLine is inside a collapsed region.
            while (rangeIdx < collapsed.Count && collapsed[rangeIdx].EndLine < docLine)
                rangeIdx++;

            if (rangeIdx < collapsed.Count &&
                docLine > collapsed[rangeIdx].StartLine &&
                docLine <= collapsed[rangeIdx].EndLine)
            {
                // Skip to end of collapsed region.
                docLine = collapsed[rangeIdx].EndLine + 1;
                rangeIdx++;
                continue;
            }

            if (visCount == visibleLine)
                return docLine;

            visCount++;
            docLine++;
        }

        return docLine;
    }

    /// <summary>
    /// Converts a document line index to a visible (display) line index.
    /// Returns -1 if the line is hidden.
    /// </summary>
    public long DocumentLineToVisibleLine(long docLine)
    {
        if (_collapsedStartLines.Count == 0)
            return docLine;

        if (!IsLineVisible(docLine))
            return -1;

        long hiddenBefore = 0;
        var collapsed = GetSortedCollapsedRanges();

        foreach (var range in collapsed)
        {
            if (range.StartLine >= docLine)
                break;

            long hiddenStart = range.StartLine + 1;
            long hiddenEnd = Math.Min(range.EndLine, docLine - 1);

            if (hiddenEnd >= hiddenStart)
                hiddenBefore += hiddenEnd - hiddenStart + 1;
        }

        return docLine - hiddenBefore;
    }

    /// <summary>
    /// Returns the total number of visible lines, accounting for collapsed regions.
    /// </summary>
    public long GetVisibleLineCount(long totalDocLines)
    {
        if (_collapsedStartLines.Count == 0)
            return totalDocLines;

        long hidden = 0;
        foreach (long startLine in _collapsedStartLines)
        {
            if (_regionByStartLine.TryGetValue(startLine, out FoldRegion region))
            {
                hidden += region.EndLine - region.StartLine;
            }
        }

        return Math.Max(1, totalDocLines - hidden);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Region detection
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects foldable regions in the document using a brace-matching
    /// strategy for C-like languages and an indent-based strategy for others.
    /// </summary>
    public void DetectFoldingRegions(PieceTable buffer, string languageId)
    {
        var regions = new List<FoldRegion>();

        if (string.Equals(languageId, "html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(languageId, "xhtml", StringComparison.OrdinalIgnoreCase))
        {
            DetectHtmlScriptStyleBraceRegions(buffer, regions);
        }
        else if (IsBraceLanguage(languageId))
        {
            DetectBraceRegions(buffer, regions);
        }
        else
        {
            DetectIndentRegions(buffer, regions);
        }

        SetRegions(regions);
    }

    /// <summary>
    /// Detects fold regions by matching '{' and '}' characters.
    /// </summary>
    private static void DetectBraceRegions(PieceTable buffer, List<FoldRegion> regions)
    {
        long lineCount = buffer.LineCount;
        var braceStack = new Stack<long>(); // stack of opening-brace line numbers

        for (long line = 0; line < lineCount; line++)
        {
            string text = buffer.GetLine(line);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{')
                {
                    braceStack.Push(line);
                }
                else if (c == '}' && braceStack.Count > 0)
                {
                    long startLine = braceStack.Pop();
                    if (line > startLine)
                    {
                        regions.Add(new FoldRegion(startLine, line));
                    }
                }
            }
        }
    }

    private static void DetectHtmlScriptStyleBraceRegions(PieceTable buffer, List<FoldRegion> regions)
    {
        long lineCount = buffer.LineCount;
        var braceStack = new Stack<long>();
        var tagStack = new Stack<(string Name, long Line)>();
        bool inScript = false;
        bool inStyle = false;
        bool inComment = false;

        static bool IsVoidTag(string name) => name is
            "area" or "base" or "br" or "col" or "embed" or "hr" or "img" or
            "input" or "link" or "meta" or "param" or "source" or "track" or "wbr";

        for (long line = 0; line < lineCount; line++)
        {
            string text = buffer.GetLine(line);
            string lower = text.ToLowerInvariant();

            // Scan for HTML tags to build fold regions.
            for (int i = 0; i < lower.Length; i++)
            {
                if (!inComment && i + 3 < lower.Length && lower[i] == '<' && lower[i + 1] == '!' &&
                    lower[i + 2] == '-' && lower[i + 3] == '-')
                {
                    inComment = true;
                    int endIdx = lower.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    if (endIdx >= 0)
                    {
                        inComment = false;
                        i = endIdx + 2;
                    }
                    else
                    {
                        break;
                    }
                    continue;
                }

                if (inComment)
                {
                    int endIdx = lower.IndexOf("-->", i, StringComparison.Ordinal);
                    if (endIdx >= 0)
                    {
                        inComment = false;
                        i = endIdx + 2;
                    }
                    else
                    {
                        break;
                    }
                    continue;
                }

                if (lower[i] != '<')
                    continue;

                int tagStart = i;
                i++;
                if (i >= lower.Length)
                    break;

                bool closing = false;
                if (lower[i] == '/')
                {
                    closing = true;
                    i++;
                }

                int nameStart = i;
                while (i < lower.Length && (char.IsLetterOrDigit(lower[i]) || lower[i] == '-' || lower[i] == ':'))
                    i++;
                if (i <= nameStart)
                    continue;

                string tagName = lower.Substring(nameStart, i - nameStart);

                int tagEnd = lower.IndexOf('>', i);
                if (tagEnd < 0)
                    break;

                bool selfClosing = false;
                for (int j = tagEnd - 1; j > tagStart; j--)
                {
                    char c = lower[j];
                    if (char.IsWhiteSpace(c))
                        continue;
                    selfClosing = c == '/';
                    break;
                }

                if (!closing)
                {
                    if (!selfClosing && !IsVoidTag(tagName))
                        tagStack.Push((tagName, line));
                }
                else
                {
                    if (tagStack.Count > 0)
                    {
                        (string Name, long Line) match = default;
                        bool found = false;
                        foreach (var entry in tagStack)
                        {
                            if (entry.Name == tagName)
                            {
                                match = entry;
                                found = true;
                                break;
                            }
                        }
                        if (found)
                        {
                            while (tagStack.Count > 0)
                            {
                                var popped = tagStack.Pop();
                                if (popped.Name == tagName)
                                    break;
                            }
                            if (line > match.Line)
                                regions.Add(new FoldRegion(match.Line, line));
                        }
                    }
                }

                i = tagEnd;
            }

            if (!inScript && !inStyle)
            {
                if (lower.Contains("<script"))
                {
                    inScript = true;
                }
                else if (lower.Contains("<style"))
                {
                    inStyle = true;
                }
            }

            if (inScript || inStyle)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '{')
                    {
                        braceStack.Push(line);
                    }
                    else if (c == '}' && braceStack.Count > 0)
                    {
                        long startLine = braceStack.Pop();
                        if (line > startLine)
                            regions.Add(new FoldRegion(startLine, line));
                    }
                }
            }

            if (inScript && lower.Contains("</script"))
            {
                inScript = false;
                braceStack.Clear();
            }

            if (inStyle && lower.Contains("</style"))
            {
                inStyle = false;
                braceStack.Clear();
            }
        }
    }

    /// <summary>
    /// Detects fold regions based on indentation changes (for Python, YAML, etc.).
    /// A region starts when indentation increases and ends when it returns to the
    /// previous level.
    /// </summary>
    private static void DetectIndentRegions(PieceTable buffer, List<FoldRegion> regions)
    {
        long lineCount = buffer.LineCount;
        if (lineCount == 0) return;

        var indentStack = new Stack<(int Indent, long Line)>();

        for (long line = 0; line < lineCount; line++)
        {
            string text = buffer.GetLine(line);
            if (string.IsNullOrWhiteSpace(text)) continue;

            int indent = GetIndentLevel(text);

            while (indentStack.Count > 0 && indent <= indentStack.Peek().Indent)
            {
                var (_, startLine) = indentStack.Pop();
                if (line - 1 > startLine)
                {
                    regions.Add(new FoldRegion(startLine, line - 1));
                }
            }

            indentStack.Push((indent, line));
        }

        // Close any remaining open regions at the end of the document.
        long lastLine = lineCount - 1;
        while (indentStack.Count > 0)
        {
            var (_, startLine) = indentStack.Pop();
            if (lastLine > startLine)
            {
                regions.Add(new FoldRegion(startLine, lastLine));
            }
        }
    }

    private static int GetIndentLevel(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else if (c == '\t') count += EditorControl.DefaultTabWidth;
            else break;
        }
        return count;
    }

    private static bool IsBraceLanguage(string languageId)
    {
        return languageId switch
        {
            "csharp" or "javascript" or "typescript" or "java" or "c" or "cpp" or
            "go" or "rust" or "php" or "css" or "json" or "swift" or "kotlin" or "dart" => true,
            _ => false,
        };
    }

    private List<FoldRegion> GetSortedCollapsedRanges()
    {
        var result = new List<FoldRegion>();
        foreach (long startLine in _collapsedStartLines)
        {
            if (_regionByStartLine.TryGetValue(startLine, out FoldRegion region))
                result.Add(region);
        }

        result.Sort((a, b) => a.StartLine.CompareTo(b.StartLine));
        return result;
    }
}
