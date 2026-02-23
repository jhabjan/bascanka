namespace Bascanka.Editor.Controls;

/// <summary>
/// Manages vertical and horizontal scroll state and coordinates with
/// <see cref="ScrollBar"/> controls attached to the editor.  Handles
/// proportional mapping for very large documents where the total line
/// count exceeds <see cref="int.MaxValue"/>.
/// </summary>
public sealed class ScrollManager
{
    /// <summary>Number of lines scrolled per mouse wheel detent.</summary>
    private static int LinesPerWheelClick => EditorControl.DefaultScrollSpeed;

    /// <summary>
    /// Maximum value used for the vertical scrollbar's range.  The actual
    /// line count may exceed this, so values are mapped proportionally.
    /// </summary>
    private const int ScrollBarMaxRange = 100_000;

    private long _firstVisibleLine;
    private int _horizontalScrollOffset;
    private long _totalLines;
    private int _maxLineWidth;
    private int _visibleLines;
    private int _visibleColumns;

    /// <summary>Raised when the scroll position changes.</summary>
    public event Action? ScrollChanged;

    // ────────────────────────────────────────────────────────────────────
    //  Properties
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Zero-based index of the first visible line.</summary>
    public long FirstVisibleLine
    {
        get => _firstVisibleLine;
        set
        {
            long clamped = Math.Clamp(value, 0, Math.Max(0, _totalLines - 1));
            if (clamped == _firstVisibleLine) return;
            _firstVisibleLine = clamped;
            SyncVerticalScrollBar();
            ScrollChanged?.Invoke();
        }
    }

    /// <summary>Column offset for horizontal scrolling.</summary>
    public int HorizontalScrollOffset
    {
        get => _horizontalScrollOffset;
        set
        {
            int clamped = Math.Max(0, value);
            if (clamped == _horizontalScrollOffset) return;
            _horizontalScrollOffset = clamped;
            SyncHorizontalScrollBar();
            ScrollChanged?.Invoke();
        }
    }

    /// <summary>The vertical scrollbar control, if attached.</summary>
    public VScrollBar? VerticalScrollbar { get; private set; }

    /// <summary>The horizontal scrollbar control, if attached.</summary>
    public HScrollBar? HorizontalScrollbar { get; private set; }

    /// <summary>Pixels per horizontal scroll click (set to CharWidth for 1-character scrolling).</summary>
    public int HorizontalSmallChange { get; set; } = 8;

    // ────────────────────────────────────────────────────────────────────
    //  Setup
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attaches the scrollbar controls to this manager and wires up their
    /// scroll events.
    /// </summary>
    public void AttachScrollBars(VScrollBar vScroll, HScrollBar hScroll)
    {
        if (VerticalScrollbar is not null)
            VerticalScrollbar.Scroll -= OnVerticalScroll;
        if (HorizontalScrollbar is not null)
            HorizontalScrollbar.Scroll -= OnHorizontalScroll;

        VerticalScrollbar = vScroll;
        HorizontalScrollbar = hScroll;

        if (VerticalScrollbar is not null)
        {
            VerticalScrollbar.Scroll += OnVerticalScroll;
            VerticalScrollbar.SmallChange = 1;
            VerticalScrollbar.LargeChange = Math.Max(1, _visibleLines);
        }

        if (HorizontalScrollbar is not null)
        {
            HorizontalScrollbar.Scroll += OnHorizontalScroll;
            HorizontalScrollbar.SmallChange = HorizontalSmallChange;
            HorizontalScrollbar.LargeChange = Math.Max(1, _visibleColumns);
        }
    }

    /// <summary>
    /// Updates the scrollbar ranges based on current document metrics.
    /// </summary>
    /// <param name="totalLines">Total number of lines in the document.</param>
    /// <param name="maxLineWidth">Pixel width of the longest visible line.</param>
    /// <param name="visibleLines">Number of lines visible in the viewport.</param>
    /// <param name="visibleColumns">Viewport width in pixels.</param>
    public void UpdateScrollBars(long totalLines, int maxLineWidth, int visibleLines, int visibleColumns)
    {
        _totalLines = totalLines;
        _maxLineWidth = maxLineWidth;
        _visibleLines = visibleLines;
        _visibleColumns = visibleColumns;

        // Clamp current position.
        _firstVisibleLine = Math.Clamp(_firstVisibleLine, 0, Math.Max(0, _totalLines - 1));

        SyncVerticalScrollBar();
        SyncHorizontalScrollBar();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Scrolling operations
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Scrolls so that the specified line is at the top of the viewport.</summary>
    public void ScrollToLine(long line)
    {
        FirstVisibleLine = line;
    }

    /// <summary>
    /// Adjusts scroll position to ensure the specified line is visible.
    /// If the line is already in the viewport, no scrolling occurs.
    /// </summary>
    public void EnsureLineVisible(long line)
    {
        if (line < _firstVisibleLine)
        {
            FirstVisibleLine = line;
        }
        else if (line >= _firstVisibleLine + _visibleLines)
        {
            FirstVisibleLine = line - _visibleLines + 1;
        }
    }

    /// <summary>
    /// Handles a mouse wheel event, scrolling the specified number of
    /// detents (positive = up, negative = down).
    /// </summary>
    public void HandleMouseWheel(int delta)
    {
        int linesToScroll = -(delta / SystemInformation.MouseWheelScrollDelta) * LinesPerWheelClick;
        FirstVisibleLine = Math.Clamp(
            _firstVisibleLine + linesToScroll,
            0,
            Math.Max(0, _totalLines - _visibleLines));
    }

    // ────────────────────────────────────────────────────────────────────
    //  Scrollbar synchronisation
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Synchronises the vertical scrollbar thumb position and range with
    /// the current scroll state.  Uses proportional mapping when the total
    /// line count exceeds <see cref="ScrollBarMaxRange"/>.
    /// </summary>
    private void SyncVerticalScrollBar()
    {
        if (VerticalScrollbar is null) return;

        long maxScroll = Math.Max(0, _totalLines - _visibleLines);
        if (maxScroll <= 0)
        {
            VerticalScrollbar.Enabled = false;
            VerticalScrollbar.Value = 0;
            VerticalScrollbar.Maximum = 0;
            return;
        }

        VerticalScrollbar.Enabled = true;

        if (_totalLines <= ScrollBarMaxRange)
        {
            // Direct mapping.
            VerticalScrollbar.Minimum = 0;
            VerticalScrollbar.Maximum = (int)(maxScroll + _visibleLines - 1);
            VerticalScrollbar.LargeChange = Math.Max(1, _visibleLines);
            VerticalScrollbar.Value = (int)Math.Min(_firstVisibleLine, maxScroll);
        }
        else
        {
            // Proportional mapping for huge files.
            VerticalScrollbar.Minimum = 0;
            VerticalScrollbar.Maximum = ScrollBarMaxRange + _visibleLines - 1;
            VerticalScrollbar.LargeChange = Math.Max(1, _visibleLines);

            double ratio = maxScroll > 0 ? (double)_firstVisibleLine / maxScroll : 0;
            VerticalScrollbar.Value = (int)(ratio * ScrollBarMaxRange);
        }
    }

    private void SyncHorizontalScrollBar()
    {
        if (HorizontalScrollbar is null) return;

        int maxScroll = Math.Max(0, _maxLineWidth - _visibleColumns + 1);
        if (maxScroll <= 0)
        {
            HorizontalScrollbar.Enabled = false;
            HorizontalScrollbar.Value = 0;
            HorizontalScrollbar.Maximum = 0;
            return;
        }

        HorizontalScrollbar.Enabled = true;
        HorizontalScrollbar.Minimum = 0;
        HorizontalScrollbar.Maximum = maxScroll + _visibleColumns - 1;
        HorizontalScrollbar.LargeChange = Math.Max(1, _visibleColumns);
        HorizontalScrollbar.Value = Math.Min(_horizontalScrollOffset, maxScroll);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Scrollbar event handlers
    // ────────────────────────────────────────────────────────────────────

    private void OnVerticalScroll(object? sender, ScrollEventArgs e)
    {
        if (VerticalScrollbar is null) return;

        long maxScroll = Math.Max(0, _totalLines - _visibleLines);

        if (_totalLines <= ScrollBarMaxRange)
        {
            _firstVisibleLine = Math.Clamp(e.NewValue, 0, maxScroll);
        }
        else
        {
            // Reverse proportional mapping.
            double ratio = ScrollBarMaxRange > 0 ? (double)e.NewValue / ScrollBarMaxRange : 0;
            _firstVisibleLine = (long)Math.Clamp(ratio * maxScroll, 0, maxScroll);
        }

        ScrollChanged?.Invoke();
    }

    private void OnHorizontalScroll(object? sender, ScrollEventArgs e)
    {
        _horizontalScrollOffset = Math.Max(0, e.NewValue);
        ScrollChanged?.Invoke();
    }
}
