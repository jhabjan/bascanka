using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Panels;

/// <summary>
/// Custom-drawn tab strip for the bottom panel with drag-reorder support.
/// Includes a far-right close button to collapse the entire panel.
/// </summary>
public class BottomPanelTabStrip : Control
{
    // ── Constants ─────────────────────────────────────────────────────
    private const int StripHeight = 28;
    private const int TabPaddingX = 10;
    private const int CloseButtonSize = 12;
    private const int CloseButtonMargin = 4;
    private const int PanelCloseSize = 14;
    private const int PanelCloseMargin = 6;
    private const int DragThreshold = 5;

    // ── State ─────────────────────────────────────────────────────────
    private readonly List<BottomPanelTab> _tabs = [];
    private int _selectedIndex = -1;
    private int _hoverTabIndex = -1;
    private int _hoverCloseIndex = -1;
    private bool _hoverPanelClose;
    private ITheme? _theme;

    // ── Drag state ────────────────────────────────────────────────────
    private int _dragTabIndex = -1;
    private Point _dragStartPoint;
    private bool _isDragging;
    private int _dragInsertIndex = -1;

    // ── Events ────────────────────────────────────────────────────────
    public event EventHandler<BottomTabEventArgs>? TabSelected;
    public event EventHandler<BottomTabEventArgs>? TabCloseRequested;
    public event EventHandler? PanelCloseRequested;

    // ── Construction ──────────────────────────────────────────────────
    public BottomPanelTabStrip()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        Height = StripHeight;
        Dock = DockStyle.Top;
    }

    // ── Public API ────────────────────────────────────────────────────

    public ITheme? Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    public void AddTab(BottomPanelTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tabs.Add(tab);
        if (_selectedIndex < 0)
            _selectedIndex = 0;
        Invalidate();
    }

    public void RemoveTab(string id)
    {
        int index = FindTabIndex(id);
        if (index < 0) return;

        _tabs.RemoveAt(index);

        if (_tabs.Count == 0)
            _selectedIndex = -1;
        else if (_selectedIndex >= _tabs.Count)
            _selectedIndex = _tabs.Count - 1;
        else if (_selectedIndex > index)
            _selectedIndex--;

        Invalidate();
    }

    public void SelectTab(string id)
    {
        int index = FindTabIndex(id);
        if (index < 0 || index == _selectedIndex) return;

        _selectedIndex = index;
        Invalidate();
        TabSelected?.Invoke(this, new BottomTabEventArgs(id));
    }

    public bool HasTab(string id) => FindTabIndex(id) >= 0;

    public void SetTabTitle(string id, string title)
    {
        int index = FindTabIndex(id);
        if (index < 0) return;
        _tabs[index].Title = title;
        Invalidate();
    }

    public string? SelectedTabId =>
        _selectedIndex >= 0 && _selectedIndex < _tabs.Count
            ? _tabs[_selectedIndex].Id
            : null;

    // ── Painting ──────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color bgColor = _theme?.TabBarBackground ?? SystemColors.Control;
        using (var bgBrush = new SolidBrush(bgColor))
            g.FillRectangle(bgBrush, ClientRectangle);

        // Draw bottom border line.
        Color borderColor = _theme?.TabBorder ?? SystemColors.ControlDark;
        using (var pen = new Pen(borderColor))
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        // Paint tabs.
        int x = 0;
        for (int i = 0; i < _tabs.Count; i++)
        {
            int tabW = MeasureTabWidth(g, i);
            var tabRect = new Rectangle(x, 0, tabW, StripHeight);
            PaintTab(g, i, tabRect);
            x += tabW;
        }

        // Draw drag insertion indicator.
        if (_isDragging && _dragInsertIndex >= 0)
        {
            int indicatorX = GetInsertIndicatorX(g);
            Color accentColor = _theme?.StatusBarBackground ?? Color.FromArgb(0, 122, 204);
            using var indicatorPen = new Pen(accentColor, 2f);
            g.DrawLine(indicatorPen, indicatorX, 2, indicatorX, StripHeight - 3);
        }

        // Paint panel close button (far right).
        PaintPanelCloseButton(g);
    }

    private void PaintTab(Graphics g, int index, Rectangle rect)
    {
        var tab = _tabs[index];
        bool isSelected = index == _selectedIndex;
        bool isHover = index == _hoverTabIndex && !_isDragging;
        bool isDragSource = _isDragging && index == _dragTabIndex;

        // Background.
        Color tabBg = isSelected
            ? (_theme?.TabActiveBackground ?? SystemColors.Window)
            : isHover
                ? Color.FromArgb(40, _theme?.TabActiveForeground ?? SystemColors.ControlText)
                : (_theme?.TabBarBackground ?? SystemColors.Control);

        // Dim the dragged tab slightly.
        if (isDragSource)
            tabBg = Color.FromArgb(120, tabBg);

        using (var brush = new SolidBrush(tabBg))
            g.FillRectangle(brush, rect);

        // Active tab accent bar at the bottom.
        if (isSelected)
        {
            Color accentColor = _theme?.StatusBarBackground ?? Color.FromArgb(0, 122, 204);
            using var accentBrush = new SolidBrush(accentColor);
            g.FillRectangle(accentBrush, rect.X, rect.Height - 2, rect.Width, 2);
        }

        // Right border.
        Color bColor = _theme?.TabBorder ?? SystemColors.ControlDark;
        using (var pen = new Pen(bColor))
            g.DrawLine(pen, rect.Right - 1, rect.Top, rect.Right - 1, rect.Bottom);

        // Title text.
        Color fgColor = isSelected
            ? (_theme?.TabActiveForeground ?? SystemColors.ControlText)
            : (_theme?.TabInactiveForeground ?? SystemColors.GrayText);

        if (isDragSource) fgColor = Color.FromArgb(120, fgColor);

        int textX = rect.X + TabPaddingX;
        int textWidth = rect.Width - TabPaddingX * 2;
        if (tab.Closable)
            textWidth -= CloseButtonSize + CloseButtonMargin;

        if (textWidth > 0)
        {
            var textRect = new Rectangle(textX, 0, textWidth, StripHeight);
            using var textBrush = new SolidBrush(fgColor);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            g.DrawString(tab.Title, Font, textBrush, textRect, sf);
        }

        // Close button (only for closable tabs, on hover or selected).
        if (tab.Closable && (isHover || isSelected) && !isDragSource)
        {
            var closeBtnRect = GetTabCloseButtonRect(rect);
            bool closeHover = index == _hoverCloseIndex;

            if (closeHover)
            {
                using var hoverBrush = new SolidBrush(Color.FromArgb(60,
                    _theme?.TabActiveForeground ?? SystemColors.ControlText));
                g.FillEllipse(hoverBrush,
                    closeBtnRect.X - 2, closeBtnRect.Y - 2,
                    closeBtnRect.Width + 4, closeBtnRect.Height + 4);
            }

            using var closePen = new Pen(fgColor, 1.5f);
            int margin = 3;
            g.DrawLine(closePen,
                closeBtnRect.X + margin, closeBtnRect.Y + margin,
                closeBtnRect.Right - margin, closeBtnRect.Bottom - margin);
            g.DrawLine(closePen,
                closeBtnRect.Right - margin, closeBtnRect.Y + margin,
                closeBtnRect.X + margin, closeBtnRect.Bottom - margin);
        }
    }

    private void PaintPanelCloseButton(Graphics g)
    {
        var rect = GetPanelCloseRect();
        Color fg = _theme?.TabActiveForeground ?? SystemColors.ControlText;

        if (_hoverPanelClose)
        {
            using var hoverBrush = new SolidBrush(Color.FromArgb(60, fg));
            g.FillEllipse(hoverBrush,
                rect.X - 2, rect.Y - 2,
                rect.Width + 4, rect.Height + 4);
        }

        using var pen = new Pen(fg, 1.5f);
        int m = 3;
        g.DrawLine(pen, rect.X + m, rect.Y + m, rect.Right - m, rect.Bottom - m);
        g.DrawLine(pen, rect.Right - m, rect.Y + m, rect.X + m, rect.Bottom - m);
    }

    // ── Mouse handling ────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        // Panel close button.
        if (GetPanelCloseRect().Contains(e.Location))
        {
            PanelCloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Tab hit test.
        int index = HitTestTab(e.Location);
        if (index < 0) return;

        // Close button on tab.
        if (_tabs[index].Closable)
        {
            var closeRect = GetTabCloseButtonRect(GetTabRectangle(index));
            var expanded = Rectangle.Inflate(closeRect, 3, 3);
            if (expanded.Contains(e.Location))
            {
                TabCloseRequested?.Invoke(this, new BottomTabEventArgs(_tabs[index].Id));
                return;
            }
        }

        // Select tab and prepare for potential drag.
        if (index != _selectedIndex)
        {
            _selectedIndex = index;
            Invalidate();
            TabSelected?.Invoke(this, new BottomTabEventArgs(_tabs[index].Id));
        }

        _dragTabIndex = index;
        _dragStartPoint = e.Location;
        _isDragging = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // Handle drag.
        if (_dragTabIndex >= 0 && e.Button == MouseButtons.Left)
        {
            if (!_isDragging)
            {
                int dx = Math.Abs(e.X - _dragStartPoint.X);
                if (dx >= DragThreshold)
                {
                    _isDragging = true;
                    Cursor = Cursors.Hand;
                }
            }

            if (_isDragging)
            {
                _dragInsertIndex = CalcInsertIndex(e.Location);
                Invalidate();
                return;
            }
        }

        // Normal hover tracking.
        int prevHover = _hoverTabIndex;
        int prevClose = _hoverCloseIndex;
        bool prevPanelClose = _hoverPanelClose;

        _hoverTabIndex = HitTestTab(e.Location);
        _hoverCloseIndex = -1;
        _hoverPanelClose = GetPanelCloseRect().Contains(e.Location);

        if (_hoverTabIndex >= 0 && _tabs[_hoverTabIndex].Closable)
        {
            var closeRect = GetTabCloseButtonRect(GetTabRectangle(_hoverTabIndex));
            var expanded = Rectangle.Inflate(closeRect, 3, 3);
            if (expanded.Contains(e.Location))
            {
                _hoverCloseIndex = _hoverTabIndex;
                Cursor = Cursors.Hand;
            }
            else
            {
                Cursor = Cursors.Default;
            }
        }
        else if (_hoverPanelClose)
        {
            Cursor = Cursors.Hand;
        }
        else
        {
            Cursor = Cursors.Default;
        }

        if (prevHover != _hoverTabIndex || prevClose != _hoverCloseIndex || prevPanelClose != _hoverPanelClose)
            Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_isDragging && _dragTabIndex >= 0 && _dragInsertIndex >= 0)
        {
            PerformReorder(_dragTabIndex, _dragInsertIndex);
        }
        CancelDrag();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        CancelDrag();
        if (_hoverTabIndex != -1 || _hoverCloseIndex != -1 || _hoverPanelClose)
        {
            _hoverTabIndex = -1;
            _hoverCloseIndex = -1;
            _hoverPanelClose = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    private void CancelDrag()
    {
        if (_isDragging || _dragTabIndex >= 0)
        {
            _isDragging = false;
            _dragTabIndex = -1;
            _dragInsertIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    // ── Drag reorder ──────────────────────────────────────────────────

    private int CalcInsertIndex(Point pt)
    {
        using Graphics g = CreateGraphics();
        int x = 0;
        for (int i = 0; i < _tabs.Count; i++)
        {
            int w = MeasureTabWidth(g, i);
            int mid = x + w / 2;
            if (pt.X < mid) return i;
            x += w;
        }
        return _tabs.Count;
    }

    private int GetInsertIndicatorX(Graphics g)
    {
        int x = 0;
        int target = Math.Clamp(_dragInsertIndex, 0, _tabs.Count);
        for (int i = 0; i < target; i++)
            x += MeasureTabWidth(g, i);
        return x;
    }

    private void PerformReorder(int fromIndex, int toInsertIndex)
    {
        if (fromIndex < 0 || fromIndex >= _tabs.Count) return;

        // Adjust insert index: if dragging right, account for removal shifting indices.
        int effectiveTo = toInsertIndex;
        if (effectiveTo > fromIndex) effectiveTo--;
        if (effectiveTo == fromIndex) return; // No change.

        var tab = _tabs[fromIndex];
        bool wasSelected = _selectedIndex == fromIndex;

        _tabs.RemoveAt(fromIndex);
        // Clamp to valid range after removal.
        effectiveTo = Math.Clamp(effectiveTo, 0, _tabs.Count);
        _tabs.Insert(effectiveTo, tab);

        // Preserve selection.
        if (wasSelected)
            _selectedIndex = effectiveTo;
        else
            _selectedIndex = FindTabIndex(_tabs[Math.Clamp(_selectedIndex, 0, _tabs.Count - 1)].Id);

        Invalidate();
    }

    // ── Hit testing / measurement ─────────────────────────────────────

    private int HitTestTab(Point pt)
    {
        using Graphics g = CreateGraphics();
        int x = 0;
        for (int i = 0; i < _tabs.Count; i++)
        {
            int w = MeasureTabWidth(g, i);
            if (new Rectangle(x, 0, w, StripHeight).Contains(pt))
                return i;
            x += w;
        }
        return -1;
    }

    private Rectangle GetTabRectangle(int index)
    {
        using Graphics g = CreateGraphics();
        int x = 0;
        for (int i = 0; i < index; i++)
            x += MeasureTabWidth(g, i);
        return new Rectangle(x, 0, MeasureTabWidth(g, index), StripHeight);
    }

    private int MeasureTabWidth(Graphics g, int index)
    {
        if (index < 0 || index >= _tabs.Count) return 80;
        var tab = _tabs[index];
        int textWidth = (int)Math.Ceiling(g.MeasureString(tab.Title, Font).Width);
        int closeArea = tab.Closable ? CloseButtonSize + CloseButtonMargin * 2 : 0;
        return TabPaddingX + textWidth + closeArea + TabPaddingX;
    }

    private static Rectangle GetTabCloseButtonRect(Rectangle tabRect)
    {
        int x = tabRect.Right - CloseButtonMargin - CloseButtonSize - 4;
        int y = (tabRect.Height - CloseButtonSize) / 2;
        return new Rectangle(x, y, CloseButtonSize, CloseButtonSize);
    }

    private Rectangle GetPanelCloseRect()
    {
        int x = Width - PanelCloseMargin - PanelCloseSize - 4;
        int y = (StripHeight - PanelCloseSize) / 2;
        return new Rectangle(x, y, PanelCloseSize, PanelCloseSize);
    }

    private int FindTabIndex(string id)
    {
        for (int i = 0; i < _tabs.Count; i++)
            if (_tabs[i].Id == id)
                return i;
        return -1;
    }
}
