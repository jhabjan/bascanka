namespace Bascanka.Editor.Tabs;

/// <summary>
/// Manages drag-to-reorder behaviour for a <see cref="TabStrip"/>.
/// <para>
/// When the user presses and holds a mouse button on a tab and then moves
/// beyond a small dead-zone threshold, the manager enters drag mode.  During
/// the drag a translucent ghost image of the tab follows the cursor, and
/// once the button is released the manager calculates the drop position and
/// raises <see cref="TabMoved"/>.
/// </para>
/// </summary>
public sealed class TabDragManager
{
    // ── Constants ─────────────────────────────────────────────────────
    private const int DragDeadZone = 10; // pixels before drag activates
    private const float GhostAlpha = 0.65f;

    // ── State ─────────────────────────────────────────────────────────
    private readonly TabStrip _tabStrip;
    private bool _mouseDown;
    private bool _isDragging;
    private int _dragTabIndex = -1;
    private Point _dragStartPoint;
    private Point _currentMousePoint;
    private Bitmap? _dragGhostBitmap;

    // ── Events ────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when a tab has been successfully moved from one position to
    /// another via drag-and-drop.
    /// </summary>
    public event EventHandler<TabMovedEventArgs>? TabMoved;

    // ── Construction ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="TabDragManager"/> that attaches to the
    /// supplied <paramref name="tabStrip"/>.
    /// </summary>
    public TabDragManager(TabStrip tabStrip)
    {
        _tabStrip = tabStrip ?? throw new ArgumentNullException(nameof(tabStrip));
        AttachEvents();
    }

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Whether a drag operation is currently in progress.
    /// </summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// The index of the tab currently being dragged, or <c>-1</c> if idle.
    /// </summary>
    public int DragTabIndex => _isDragging ? _dragTabIndex : -1;

    /// <summary>
    /// The current mouse position during a drag (in tab-strip client
    /// coordinates).  Used by <see cref="TabStrip"/> during painting to
    /// draw the ghost image.
    /// </summary>
    public Point CurrentMousePoint => _currentMousePoint;

    /// <summary>
    /// Cancels any in-progress drag operation without raising
    /// <see cref="TabMoved"/>.
    /// </summary>
    public void CancelDrag()
    {
        if (!_isDragging) return;
        EndDrag(cancelled: true);
    }

    // ── Paint helper ──────────────────────────────────────────────────

    /// <summary>
    /// Paints the translucent ghost of the dragged tab at the current
    /// mouse position.  Called from <see cref="TabStrip.OnPaint"/>.
    /// </summary>
    public void PaintDragGhost(Graphics g)
    {
        if (!_isDragging || _dragGhostBitmap is null) return;

        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        float[][] matrixItems =
        [
            [1, 0, 0, 0, 0],
            [0, 1, 0, 0, 0],
            [0, 0, 1, 0, 0],
            [0, 0, 0, GhostAlpha, 0],
            [0, 0, 0, 0, 1],
        ];
        var colourMatrix = new System.Drawing.Imaging.ColorMatrix(matrixItems);
        attributes.SetColorMatrix(colourMatrix);

        int x = _currentMousePoint.X - _dragGhostBitmap.Width / 2;
        int y = 0;
        var destRect = new Rectangle(x, y, _dragGhostBitmap.Width, _dragGhostBitmap.Height);

        g.DrawImage(
            _dragGhostBitmap,
            destRect,
            0, 0, _dragGhostBitmap.Width, _dragGhostBitmap.Height,
            GraphicsUnit.Pixel,
            attributes);
    }

    // ── Event wiring ──────────────────────────────────────────────────

    private void AttachEvents()
    {
        _tabStrip.MouseDown += OnMouseDown;
        _tabStrip.MouseMove += OnMouseMove;
        _tabStrip.MouseUp += OnMouseUp;
        _tabStrip.MouseLeave += OnMouseLeave;
    }

    // ── Mouse handlers ────────────────────────────────────────────────

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        int index = _tabStrip.HitTestTab(e.Location);
        if (index < 0) return;

        _mouseDown = true;
        _dragTabIndex = index;
        _dragStartPoint = e.Location;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_mouseDown) return;

        _currentMousePoint = e.Location;

        if (!_isDragging)
        {
            int dx = Math.Abs(e.X - _dragStartPoint.X);
            int dy = Math.Abs(e.Y - _dragStartPoint.Y);
            if (dx > DragDeadZone || dy > DragDeadZone)
            {
                // Only start a drag if the cursor has moved over a
                // different tab — prevents accidental reorders from
                // small mouse movements within the same tab.
                int hoverIndex = _tabStrip.HitTestTab(e.Location);
                if (hoverIndex >= 0 && hoverIndex != _dragTabIndex)
                    BeginDrag();
            }
        }

        if (_isDragging)
        {
            _tabStrip.Invalidate();
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_mouseDown) return;

        if (_isDragging)
        {
            int dropIndex = CalculateDropIndex(e.Location);
            EndDrag(cancelled: false);

            if (dropIndex >= 0 && dropIndex != _dragTabIndex)
            {
                TabMoved?.Invoke(this, new TabMovedEventArgs(_dragTabIndex, dropIndex));
            }
        }

        _mouseDown = false;
        _dragTabIndex = -1;
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        if (_isDragging)
        {
            EndDrag(cancelled: true);
        }

        _mouseDown = false;
        _dragTabIndex = -1;
    }

    // ── Drag lifecycle ────────────────────────────────────────────────

    private void BeginDrag()
    {
        _isDragging = true;
        _tabStrip.Capture = true;
        CreateGhostBitmap();
    }

    private void EndDrag(bool cancelled)
    {
        _isDragging = false;
        _tabStrip.Capture = false;
		_dragGhostBitmap?.Dispose();
		_dragGhostBitmap = null;

		_tabStrip.Invalidate();

        if (cancelled)
        {
            _mouseDown = false;
            _dragTabIndex = -1;
        }
    }

    /// <summary>
    /// Captures the visual appearance of the tab at <see cref="_dragTabIndex"/>
    /// into a bitmap for painting as a ghost during the drag operation.
    /// </summary>
    private void CreateGhostBitmap()
    {
        Rectangle tabRect = _tabStrip.GetTabRectangle(_dragTabIndex);
        if (tabRect.Width <= 0 || tabRect.Height <= 0) return;

        _dragGhostBitmap = new Bitmap(tabRect.Width, tabRect.Height);
        using Graphics g = Graphics.FromImage(_dragGhostBitmap);
        g.TranslateTransform(-tabRect.X, -tabRect.Y);

        // Ask the tab strip to paint just this tab into the bitmap.
        _tabStrip.PaintSingleTab(g, _dragTabIndex);
    }

    /// <summary>
    /// Determines which tab index the mouse position corresponds to, based on
    /// the midpoints of visible tabs.  Returns the insertion index (0-based).
    /// </summary>
    private int CalculateDropIndex(Point mouseLocation)
    {
        int count = _tabStrip.Tabs.Count;
        if (count == 0) return 0;

        for (int i = 0; i < count; i++)
        {
            Rectangle rect = _tabStrip.GetTabRectangle(i);
            int midX = rect.X + rect.Width / 2;

            if (mouseLocation.X < midX)
                return i;
        }

        return count - 1;
    }
}
