using System.Diagnostics;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Tabs;

/// <summary>
/// Event arguments for tab-related events that carry an index.
/// </summary>
public sealed class TabEventArgs : EventArgs
{
    /// <summary>Zero-based index of the affected tab.</summary>
    public int Index { get; }

    public TabEventArgs(int index) => Index = index;
}

/// <summary>
/// Event arguments raised before a tab context menu is displayed, allowing
/// consumers to customise the menu.
/// </summary>
public sealed class TabContextMenuOpeningEventArgs : EventArgs
{
    /// <summary>Zero-based index of the tab that was right-clicked.</summary>
    public int Index { get; }

    /// <summary>The context menu about to be shown.  Handlers may add items.</summary>
    public ContextMenuStrip Menu { get; }

    public TabContextMenuOpeningEventArgs(int index, ContextMenuStrip menu)
    {
        Index = index;
        Menu = menu;
    }
}

/// <summary>
/// A custom-drawn tab strip that displays a horizontal row of document tabs.
/// <para>
/// Features include:
/// <list type="bullet">
///   <item>Modified-document indicator (* prefix).</item>
///   <item>Per-tab close button that appears on hover.</item>
///   <item>Horizontal scrolling when tabs overflow the visible width.</item>
///   <item>Mouse: left-click to select, middle-click to close, right-click
///         for context menu.</item>
///   <item>Drag-to-reorder via <see cref="TabDragManager"/>.</item>
///   <item>Theme-aware rendering driven by an <see cref="ITheme"/>.</item>
/// </list>
/// </para>
/// </summary>
public class TabStrip : Control
{
    // ── Constants ─────────────────────────────────────────────────────
    /// <summary>Configurable tab height in pixels (default 30).</summary>
    public static int ConfigTabHeight { get; set; } = 30;
    private const int TabPaddingX = 12;
    private const int CloseButtonSize = 14;
    private const int CloseButtonMargin = 4;
    private const int ScrollArrowWidth = 20;

    /// <summary>Configurable minimum tab width in pixels (default 80).</summary>
    public static int ConfigMinTabWidth { get; set; } = 80;

    /// <summary>Configurable maximum tab width in pixels (default 220).</summary>
    public static int ConfigMaxTabWidth { get; set; } = 220;

    // ── State ─────────────────────────────────────────────────────────
    private readonly List<TabInfo> _tabs = [];
    private int _selectedIndex = -1;
    private int _scrollOffset;
    private int _hoverTabIndex = -1;
    private int _hoverCloseIndex = -1;
    private ITheme? _theme;
    private TabDragManager? _dragManager;
    private readonly ContextMenuStrip _contextMenu;

    // ── Events ────────────────────────────────────────────────────────

    /// <summary>Raised when the user selects a different tab.</summary>
    public event EventHandler<TabEventArgs>? TabSelected;

    /// <summary>Raised when a tab is closed (via close button or context menu).</summary>
    public event EventHandler<TabEventArgs>? TabClosed;

    /// <summary>Raised after tabs are reordered via drag-and-drop.</summary>
    public event EventHandler? TabsReordered;

    /// <summary>
    /// Raised when a tab's context menu is about to open, allowing
    /// consumers to customise the menu.
    /// </summary>
    public event EventHandler<TabContextMenuOpeningEventArgs>? TabContextMenuOpening;

    /// <summary>Raised when the user double-clicks the empty area of the tab strip.</summary>
    public event EventHandler? NewTabRequested;

    // ── Construction ──────────────────────────────────────────────────

    public TabStrip()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        Height = ConfigTabHeight;

        _contextMenu = BuildContextMenu();
        _dragManager = new TabDragManager(this);
        _dragManager.TabMoved += OnDragManagerTabMoved;
    }

    // ── Public properties ─────────────────────────────────────────────

    /// <summary>
    /// The ordered list of open tabs.
    /// </summary>
    public List<TabInfo> Tabs => _tabs;

    /// <summary>
    /// Index of the currently selected (active) tab, or <c>-1</c> when no
    /// tab is selected.
    /// </summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= _tabs.Count)
                throw new ArgumentOutOfRangeException(nameof(value));

            if (_selectedIndex == value) return;
            _selectedIndex = value;
            EnsureTabVisible(_selectedIndex);
            Invalidate();
            TabSelected?.Invoke(this, new TabEventArgs(_selectedIndex));
        }
    }

    /// <summary>
    /// The currently selected <see cref="TabInfo"/>, or <see langword="null"/>
    /// when no tab is active.
    /// </summary>
    public TabInfo? SelectedTab =>
        _selectedIndex >= 0 && _selectedIndex < _tabs.Count
            ? _tabs[_selectedIndex]
            : null;

    /// <summary>
    /// The theme used for rendering.  When set, the control is invalidated
    /// so that the new colours take effect immediately.
    /// </summary>
    public Func<ITheme, ToolStripRenderer>? ContextMenuRenderer { get; set; }

    public ITheme? Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            if (value is not null && ContextMenuRenderer is not null)
                _contextMenu.Renderer = ContextMenuRenderer(value);
            Invalidate();
        }
    }

    /// <summary>
    /// Raised when the user requests to close a tab (via close button or context menu).
    /// This is an <c>EventHandler&lt;int&gt;</c> alias for <see cref="TabClosed"/>.
    /// </summary>
    public event EventHandler<int>? TabCloseRequested;

    /// <summary>Sets the active tab by index (alias for <see cref="SelectedIndex"/>).</summary>
    public void SetActiveTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        _selectedIndex = index;
        EnsureTabVisible(index);
        Invalidate();
    }

    /// <summary>Updates the display title for the tab at the given index.</summary>
    public void UpdateTab(int index, string title)
    {
        if (index < 0 || index >= _tabs.Count) return;
        Invalidate();
    }

    // ── Tab management ────────────────────────────────────────────────

    /// <summary>
    /// Appends a new tab and optionally selects it.
    /// </summary>
    public void AddTab(TabInfo tab, bool select = true)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _tabs.Add(tab);

        if (select)
        {
            _selectedIndex = _tabs.Count - 1;
            EnsureTabVisible(_selectedIndex);
        }

        Invalidate();
    }

    /// <summary>
    /// Inserts a tab at the specified index.
    /// </summary>
    public void InsertTab(int index, TabInfo tab, bool select = true)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (index < 0 || index > _tabs.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _tabs.Insert(index, tab);

        // Adjust selected index if it shifted.
        if (_selectedIndex >= index)
            _selectedIndex++;

        if (select)
        {
            _selectedIndex = index;
            EnsureTabVisible(_selectedIndex);
        }

        Invalidate();
    }

    /// <summary>
    /// Removes the tab at the specified index.  If the removed tab was
    /// selected, the selection moves to the nearest neighbour.
    /// </summary>
    public void RemoveTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        _tabs.RemoveAt(index);

        if (_tabs.Count == 0)
        {
            _selectedIndex = -1;
        }
        else if (_selectedIndex >= _tabs.Count)
        {
            _selectedIndex = _tabs.Count - 1;
        }
        else if (_selectedIndex > index)
        {
            _selectedIndex--;
        }

        Invalidate();
    }

    /// <summary>
    /// Removes a tab by reference.
    /// </summary>
    public void RemoveTab(TabInfo tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        int index = _tabs.IndexOf(tab);
        if (index >= 0)
            RemoveTab(index);
    }

    // ── Hit testing (used by TabDragManager) ──────────────────────────

    /// <summary>
    /// Returns the zero-based tab index at the given point, or <c>-1</c> if
    /// the point does not fall on any tab.
    /// </summary>
    public int HitTestTab(Point pt)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            Rectangle r = GetTabRectangle(i);
            if (r.Contains(pt))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Returns the bounding rectangle for the tab at <paramref name="index"/>
    /// in client coordinates (taking the scroll offset into account).
    /// </summary>
    public Rectangle GetTabRectangle(int index)
    {
        if (index < 0 || index >= _tabs.Count)
            return Rectangle.Empty;

        int x = -_scrollOffset + ScrollAreaLeft;
        for (int i = 0; i < index; i++)
            x += MeasureTabWidth(i);

        return new Rectangle(x, 0, MeasureTabWidth(index), ConfigTabHeight);
    }

    /// <summary>
    /// Paints a single tab into the given <see cref="Graphics"/> surface.
    /// Called by <see cref="TabDragManager"/> to create ghost bitmaps.
    /// </summary>
    public void PaintSingleTab(Graphics g, int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        Rectangle rect = GetTabRectangle(index);
        PaintTab(g, index, rect, isHover: false);
    }

    // ── Painting ──────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Color bgColor = _theme?.TabBarBackground ?? SystemColors.Control;
        using (var bgBrush = new SolidBrush(bgColor))
            g.FillRectangle(bgBrush, ClientRectangle);

        // Paint scroll arrows if needed.
        bool needsScroll = TotalTabsWidth > ScrollAreaWidth;
        if (needsScroll)
        {
            PaintScrollArrow(g, isLeft: true);
            PaintScrollArrow(g, isLeft: false);
        }

        // Clip to the tab area to avoid drawing over scroll arrows.
        Rectangle clipRect = new(ScrollAreaLeft, 0, ScrollAreaWidth, ConfigTabHeight);
        g.SetClip(clipRect);

        // Paint tabs.
        int x = -_scrollOffset + ScrollAreaLeft;
        for (int i = 0; i < _tabs.Count; i++)
        {
            int tabW = MeasureTabWidth(i);
            Rectangle tabRect = new(x, 0, tabW, ConfigTabHeight);

            bool isHover = i == _hoverTabIndex;
            PaintTab(g, i, tabRect, isHover);

            x += tabW;
        }

        g.ResetClip();

        // Drag ghost overlay.
        _dragManager?.PaintDragGhost(g);
    }

    private void PaintTab(Graphics g, int index, Rectangle rect, bool isHover)
    {
        TabInfo tab = _tabs[index];
        bool isSelected = index == _selectedIndex;

        // Background.
        Color tabBg = isSelected
            ? (_theme?.TabActiveBackground ?? SystemColors.Window)
            : isHover
                ? Color.FromArgb(40,
                    _theme?.TabActiveForeground ?? SystemColors.ControlText)
                : (_theme?.TabInactiveBackground ?? SystemColors.Control);

        using (var brush = new SolidBrush(tabBg))
            g.FillRectangle(brush, rect);

        // Border.
        Color borderColor = _theme?.TabBorder ?? SystemColors.ControlDark;
        using (var pen = new Pen(borderColor))
        {
            g.DrawLine(pen, rect.Right - 1, rect.Top, rect.Right - 1, rect.Bottom);
        }

        // Active tab accent bar at the top.
        if (isSelected)
        {
            Color accentColor = _theme?.StatusBarBackground ?? Color.FromArgb(0, 122, 204);
            using var accentBrush = new SolidBrush(accentColor);
            g.FillRectangle(accentBrush, rect.X, rect.Y, rect.Width, 2);
        }

        // Title text.
        Color fgColor = isSelected
            ? (_theme?.TabActiveForeground ?? SystemColors.ControlText)
            : (_theme?.TabInactiveForeground ?? SystemColors.GrayText);

        string displayText = tab.Title;
        if (tab.IsModified)
        {
            // Draw the modified dot indicator.
            Color modColor = _theme?.ModifiedIndicator ?? Color.Orange;
            int dotSize = 6;
            int dotX = rect.X + TabPaddingX / 2 - dotSize / 2 + 2;
            int dotY = rect.Y + (ConfigTabHeight - dotSize) / 2;
            using var dotBrush = new SolidBrush(modColor);
            g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
        }

        int textX = rect.X + TabPaddingX + (tab.IsModified ? 6 : 0);
        int closeAreaWidth = CloseButtonSize + CloseButtonMargin * 2;
        int textWidth = rect.Width - TabPaddingX - closeAreaWidth - (tab.IsModified ? 6 : 0);

        if (textWidth > 0)
        {
            var textRect = new Rectangle(textX, 0, textWidth, ConfigTabHeight);
            using var textBrush = new SolidBrush(fgColor);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            g.DrawString(displayText, Font, textBrush, textRect, sf);
        }

        // Close button (only on hover or selected tab).
        if (isHover || isSelected)
        {
            Rectangle closeBtnRect = GetCloseButtonRect(rect);
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

    private void PaintScrollArrow(Graphics g, bool isLeft)
    {
        Rectangle rect = isLeft
            ? new Rectangle(0, 0, ScrollArrowWidth, ConfigTabHeight)
            : new Rectangle(Width - ScrollArrowWidth, 0, ScrollArrowWidth, ConfigTabHeight);

        Color bg = _theme?.TabBarBackground ?? SystemColors.Control;
        using (var brush = new SolidBrush(bg))
            g.FillRectangle(brush, rect);

        Color fg = _theme?.TabActiveForeground ?? SystemColors.ControlText;
        using var arrowPen = new Pen(fg, 1.5f);
        int cx = rect.X + rect.Width / 2;
        int cy = rect.Y + rect.Height / 2;
        int arrowH = 5;

        if (isLeft)
        {
            g.DrawLine(arrowPen, cx + arrowH / 2, cy - arrowH, cx - arrowH / 2, cy);
            g.DrawLine(arrowPen, cx - arrowH / 2, cy, cx + arrowH / 2, cy + arrowH);
        }
        else
        {
            g.DrawLine(arrowPen, cx - arrowH / 2, cy - arrowH, cx + arrowH / 2, cy);
            g.DrawLine(arrowPen, cx + arrowH / 2, cy, cx - arrowH / 2, cy + arrowH);
        }
    }

    // ── Mouse handling ────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // Check scroll arrows first.
        if (TotalTabsWidth > ScrollAreaWidth)
        {
            if (e.X < ScrollArrowWidth)
            {
                ScrollLeft();
                return;
            }
            if (e.X > Width - ScrollArrowWidth)
            {
                ScrollRight();
                return;
            }
        }

        int index = HitTestTab(e.Location);
        if (index < 0) return;

        // Close button click.
        if (e.Button == MouseButtons.Left)
        {
            Rectangle closeRect = GetCloseButtonRect(GetTabRectangle(index));
            Rectangle expandedClose = Rectangle.Inflate(closeRect, 3, 3);
            if (expandedClose.Contains(e.Location))
            {
                TabClosed?.Invoke(this, new TabEventArgs(index));
                return;
            }
        }

        switch (e.Button)
        {
            case MouseButtons.Left:
                SelectedIndex = index;
                break;

            case MouseButtons.Middle:
                TabClosed?.Invoke(this, new TabEventArgs(index));
                break;

            case MouseButtons.Right:
                SelectedIndex = index;
                ShowContextMenu(index, e.Location);
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int prevHover = _hoverTabIndex;
        int prevCloseHover = _hoverCloseIndex;

        _hoverTabIndex = HitTestTab(e.Location);
        _hoverCloseIndex = -1;

        if (_hoverTabIndex >= 0)
        {
            Rectangle closeRect = GetCloseButtonRect(GetTabRectangle(_hoverTabIndex));
            Rectangle expandedClose = Rectangle.Inflate(closeRect, 3, 3);
            if (expandedClose.Contains(e.Location))
            {
                _hoverCloseIndex = _hoverTabIndex;
                Cursor = Cursors.Hand;
            }
            else
            {
                Cursor = Cursors.Default;
            }
        }
        else
        {
            Cursor = Cursors.Default;
        }

        if (prevHover != _hoverTabIndex || prevCloseHover != _hoverCloseIndex)
            Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverTabIndex != -1 || _hoverCloseIndex != -1)
        {
            _hoverTabIndex = -1;
            _hoverCloseIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitTestTab(e.Location) < 0)
            NewTabRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (TotalTabsWidth > ScrollAreaWidth)
        {
            _scrollOffset -= e.Delta / 3;
            ClampScrollOffset();
            Invalidate();
        }
    }

    // ── Context menu ──────────────────────────────────────────────────

    private ToolStripMenuItem _menuClose = null!;
    private ToolStripMenuItem _menuCloseOthers = null!;
    private ToolStripMenuItem _menuCloseAll = null!;
    private ToolStripMenuItem _menuCloseToRight = null!;
    private ToolStripMenuItem _menuCopyPath = null!;
    private ToolStripMenuItem _menuOpenInExplorer = null!;

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        _menuClose = new ToolStripMenuItem("Close");
        _menuClose.Click += (_, _) => ContextMenuAction_Close();

        _menuCloseOthers = new ToolStripMenuItem("Close Others");
        _menuCloseOthers.Click += (_, _) => ContextMenuAction_CloseOthers();

        _menuCloseAll = new ToolStripMenuItem("Close All");
        _menuCloseAll.Click += (_, _) => ContextMenuAction_CloseAll();

        _menuCloseToRight = new ToolStripMenuItem("Close to the Right");
        _menuCloseToRight.Click += (_, _) => ContextMenuAction_CloseToRight();

        var separator = new ToolStripSeparator();

        _menuCopyPath = new ToolStripMenuItem("Copy Path");
        _menuCopyPath.Click += (_, _) => ContextMenuAction_CopyPath();

        _menuOpenInExplorer = new ToolStripMenuItem("Open Path in Explorer");
        _menuOpenInExplorer.Click += (_, _) => ContextMenuAction_OpenInExplorer();

        menu.Items.AddRange([_menuClose, _menuCloseOthers, _menuCloseAll,
                             _menuCloseToRight, separator, _menuCopyPath, _menuOpenInExplorer]);

        return menu;
    }

    /// <summary>
    /// Updates context menu text for localization.
    /// </summary>
    public void SetMenuTexts(string close, string closeOthers, string closeAll,
        string closeToRight, string copyPath, string openInExplorer)
    {
        _menuClose.Text = close;
        _menuCloseOthers.Text = closeOthers;
        _menuCloseAll.Text = closeAll;
        _menuCloseToRight.Text = closeToRight;
        _menuCopyPath.Text = copyPath;
        _menuOpenInExplorer.Text = openInExplorer;
    }

    private void ShowContextMenu(int index, Point location)
    {
        _contextMenu.Tag = index;

        bool hasPath = _tabs[index].FilePath is not null;
        _menuCopyPath.Enabled = hasPath;
        _menuOpenInExplorer.Enabled = hasPath;
        _menuCloseOthers.Enabled = _tabs.Count > 1;
        _menuCloseToRight.Enabled = index < _tabs.Count - 1;

        TabContextMenuOpening?.Invoke(this,
            new TabContextMenuOpeningEventArgs(index, _contextMenu));

        _contextMenu.Show(this, location);
    }

    private int ContextMenuTargetIndex =>
        _contextMenu.Tag is int idx ? idx : _selectedIndex;

    private void ContextMenuAction_Close()
    {
        int index = ContextMenuTargetIndex;
        if (index >= 0 && index < _tabs.Count)
            TabClosed?.Invoke(this, new TabEventArgs(index));
    }

    private void ContextMenuAction_CloseOthers()
    {
        int keepIndex = ContextMenuTargetIndex;
        if (keepIndex < 0 || keepIndex >= _tabs.Count) return;

        for (int i = _tabs.Count - 1; i >= 0; i--)
        {
            if (i != keepIndex)
                TabClosed?.Invoke(this, new TabEventArgs(i));
        }
    }

    private void ContextMenuAction_CloseAll()
    {
        for (int i = _tabs.Count - 1; i >= 0; i--)
            TabClosed?.Invoke(this, new TabEventArgs(i));
    }

    private void ContextMenuAction_CloseToRight()
    {
        int startIndex = ContextMenuTargetIndex;
        if (startIndex < 0) return;

        for (int i = _tabs.Count - 1; i > startIndex; i--)
            TabClosed?.Invoke(this, new TabEventArgs(i));
    }

    private void ContextMenuAction_CopyPath()
    {
        int index = ContextMenuTargetIndex;
        if (index < 0 || index >= _tabs.Count) return;

        string? path = _tabs[index].FilePath;
        if (path is not null)
            Clipboard.SetText(path);
    }

    private void ContextMenuAction_OpenInExplorer()
    {
        int index = ContextMenuTargetIndex;
        if (index < 0 || index >= _tabs.Count) return;

        string? path = _tabs[index].FilePath;
        if (path is not null && File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    // ── Drag-and-drop ─────────────────────────────────────────────────

    private void OnDragManagerTabMoved(object? sender, TabMovedEventArgs e)
    {
        if (e.FromIndex < 0 || e.FromIndex >= _tabs.Count) return;
        if (e.ToIndex < 0 || e.ToIndex >= _tabs.Count) return;
        if (e.FromIndex == e.ToIndex) return;

        TabInfo tab = _tabs[e.FromIndex];
        _tabs.RemoveAt(e.FromIndex);

        int insertAt = e.ToIndex > e.FromIndex ? e.ToIndex : e.ToIndex;
        if (insertAt > _tabs.Count) insertAt = _tabs.Count;
        _tabs.Insert(insertAt, tab);

        // Adjust selection to follow the moved tab if it was selected.
        if (_selectedIndex == e.FromIndex)
        {
            _selectedIndex = insertAt;
        }
        else if (e.FromIndex < _selectedIndex && insertAt >= _selectedIndex)
        {
            _selectedIndex--;
        }
        else if (e.FromIndex > _selectedIndex && insertAt <= _selectedIndex)
        {
            _selectedIndex++;
        }

        Invalidate();
        TabsReordered?.Invoke(this, EventArgs.Empty);
    }

    // ── Scroll logic ──────────────────────────────────────────────────

    private int ScrollAreaLeft => TotalTabsWidth > ScrollAreaWidth ? ScrollArrowWidth : 0;

    private int ScrollAreaWidth
    {
        get
        {
            bool needsScroll = TotalTabsWidth > Width;
            return needsScroll ? Width - ScrollArrowWidth * 2 : Width;
        }
    }

    private int TotalTabsWidth
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _tabs.Count; i++)
                total += MeasureTabWidth(i);
            return total;
        }
    }

    private void ScrollLeft()
    {
        _scrollOffset = Math.Max(0, _scrollOffset - 100);
        Invalidate();
    }

    private void ScrollRight()
    {
        int maxScroll = Math.Max(0, TotalTabsWidth - ScrollAreaWidth);
        _scrollOffset = Math.Min(maxScroll, _scrollOffset + 100);
        Invalidate();
    }

    private void ClampScrollOffset()
    {
        int maxScroll = Math.Max(0, TotalTabsWidth - ScrollAreaWidth);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxScroll);
    }

    private void EnsureTabVisible(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        if (TotalTabsWidth <= ScrollAreaWidth)
        {
            _scrollOffset = 0;
            return;
        }

        int tabStart = 0;
        for (int i = 0; i < index; i++)
            tabStart += MeasureTabWidth(i);

        int tabEnd = tabStart + MeasureTabWidth(index);

        if (tabStart < _scrollOffset)
        {
            _scrollOffset = tabStart;
        }
        else if (tabEnd > _scrollOffset + ScrollAreaWidth)
        {
            _scrollOffset = tabEnd - ScrollAreaWidth;
        }

        ClampScrollOffset();
    }

    // ── Measurement helpers ───────────────────────────────────────────

    private int MeasureTabWidth(int index)
    {
        if (index < 0 || index >= _tabs.Count) return ConfigMinTabWidth;

        TabInfo tab = _tabs[index];
        string text = tab.Title;

        int textWidth;
        using (Graphics g = CreateGraphics())
        {
            SizeF size = g.MeasureString(text, Font);
            textWidth = (int)Math.Ceiling(size.Width);
        }

        int modifiedExtra = tab.IsModified ? 10 : 0;
        int closeArea = CloseButtonSize + CloseButtonMargin * 2;
        int totalWidth = TabPaddingX + modifiedExtra + textWidth + closeArea + TabPaddingX;

        return Math.Clamp(totalWidth, ConfigMinTabWidth, ConfigMaxTabWidth);
    }

    private static Rectangle GetCloseButtonRect(Rectangle tabRect)
    {
        int x = tabRect.Right - CloseButtonMargin - CloseButtonSize - 4;
        int y = (tabRect.Height - CloseButtonSize) / 2;
        return new Rectangle(x, y, CloseButtonSize, CloseButtonSize);
    }

    // ── Disposal ──────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _contextMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
