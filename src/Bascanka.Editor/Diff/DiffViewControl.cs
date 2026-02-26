using System.Drawing;
using Bascanka.Core.Diff;
using Bascanka.Editor.Controls;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Diff;

/// <summary>
/// A composite control that displays a side-by-side diff view.
/// Contains a toolbar for navigation and two synchronised <see cref="EditorControl"/> instances.
/// </summary>
public sealed class DiffViewControl : UserControl
{
    private readonly EditorControl _leftEditor;
    private readonly EditorControl _rightEditor;
    private readonly SplitContainer _splitContainer;
    private readonly Panel _toolbar;
    private readonly Panel _titleBar;
    private readonly Label _leftTitleLabel;
    private readonly Label _rightTitleLabel;

    private readonly ToolbarButton _prevButton;
    private readonly ToolbarButton _nextButton;
    private readonly Label _diffLabel;

    private DiffResult? _result;
    private int _currentDiffIndex = -1;
    private bool _suppressScrollSync;
    private bool _suppressZoomSync;

    // Theme colours cached for owner-draw.
    private Color _toolbarBg;
    private Color _toolbarFg;
    private Color _buttonHoverBg;
    private Color _separatorColor;

    public DiffViewControl()
    {
        SuspendLayout();

        // ── Top toolbar (navigation) ─────────────────────────────────
        _toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(8, 0, 8, 0),
        };
        _toolbar.Paint += PaintToolbarBorder;

        _prevButton = new ToolbarButton
        {
            Text = "\u25C0  Prev",
            Height = 26,
            Top = 5,
            Left = 8,
            Cursor = Cursors.Hand,
        };
        _prevButton.Click += (_, _) => NavigatePrev();

        _nextButton = new ToolbarButton
        {
            Text = "Next  \u25B6",
            Height = 26,
            Top = 5,
            Cursor = Cursors.Hand,
        };
        _nextButton.Click += (_, _) => NavigateNext();

        _diffLabel = new Label
        {
            AutoSize = true,
            Top = 10,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _toolbar.Controls.AddRange([_prevButton, _nextButton, _diffLabel]);

        // ── Title bar (file names above the split) ───────────────────
        _titleBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
        };
        _titleBar.Paint += PaintToolbarBorder;

        _leftTitleLabel = new Label
        {
            AutoSize = false,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            AutoEllipsis = true,
        };

        _rightTitleLabel = new Label
        {
            AutoSize = false,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            AutoEllipsis = true,
        };

        _titleBar.Controls.AddRange([_leftTitleLabel, _rightTitleLabel]);
        _titleBar.Resize += (_, _) => LayoutTitleLabels();

        // ── Editors ──────────────────────────────────────────────────
        _leftEditor = new EditorControl { ReadOnly = true };
        _rightEditor = new EditorControl { ReadOnly = true };

        _splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.None,
            BorderStyle = BorderStyle.None,
        };

        _leftEditor.Dock = DockStyle.Fill;
        _rightEditor.Dock = DockStyle.Fill;

        _splitContainer.Panel1.Controls.Add(_leftEditor);
        _splitContainer.Panel2.Controls.Add(_rightEditor);

        // Order matters: Fill first, then Top items (last added Top docks highest).
        Controls.Add(_splitContainer);
        Controls.Add(_titleBar);
        Controls.Add(_toolbar);

        ResumeLayout(true);

        // Set splitter to 50% after layout.
        _splitContainer.SplitterDistance = _splitContainer.Width / 2;
        _splitContainer.SplitterMoved += (_, _) => LayoutTitleLabels();

        // Measure button widths and lay them out.
        LayoutToolbarButtons();

        // Wire up synchronized scrolling.
        _leftEditor.ScrollMgr.ScrollChanged += OnLeftScrollChanged;
        _rightEditor.ScrollMgr.ScrollChanged += OnRightScrollChanged;

        // Wire up synchronized zooming.
        _leftEditor.ZoomChanged += OnLeftZoomChanged;
        _rightEditor.ZoomChanged += OnRightZoomChanged;
    }

    /// <summary>
    /// Loads a diff result into the view, populating both editors.
    /// </summary>
    public void LoadDiff(DiffResult result)
    {
        _result = result;

        _leftEditor.LoadText(result.Left.PaddedText);
        _leftEditor.ReadOnly = true;
        _leftEditor.DiffLineMarkers = result.Left.Lines;

        _rightEditor.LoadText(result.Right.PaddedText);
        _rightEditor.ReadOnly = true;
        _rightEditor.DiffLineMarkers = result.Right.Lines;

        _leftTitleLabel.Text = result.Left.Title;
        _rightTitleLabel.Text = result.Right.Title;

        if (result.DiffCount > 0)
        {
            _currentDiffIndex = 0;
            NavigateToCurrentDiff();
        }
        else
        {
            _currentDiffIndex = -1;
            UpdateDiffLabel();
        }
    }

    /// <summary>
    /// Applies a theme to the diff view including both editors and the toolbar.
    /// </summary>
    public void ApplyTheme(ITheme theme)
    {
        _leftEditor.Theme = theme;
        _rightEditor.Theme = theme;

        _toolbarBg = theme.TabBarBackground;
        _toolbarFg = theme.TabActiveForeground;
        _buttonHoverBg = theme.TabInactiveBackground;
        _separatorColor = Color.FromArgb(40, theme.TabActiveForeground);

        // Toolbar
        _toolbar.BackColor = _toolbarBg;
        _diffLabel.ForeColor = theme.TabInactiveForeground;
        _diffLabel.BackColor = _toolbarBg;

        _prevButton.NormalBg = _toolbarBg;
        _prevButton.HoverBg = _buttonHoverBg;
        _prevButton.ForeColor = _toolbarFg;
        _prevButton.BackColor = _toolbarBg;
        _prevButton.BorderColor = Color.FromArgb(60, theme.TabActiveForeground);
        _prevButton.Invalidate();

        _nextButton.NormalBg = _toolbarBg;
        _nextButton.HoverBg = _buttonHoverBg;
        _nextButton.ForeColor = _toolbarFg;
        _nextButton.BackColor = _toolbarBg;
        _nextButton.BorderColor = Color.FromArgb(60, theme.TabActiveForeground);
        _nextButton.Invalidate();

        // Title bar
        _titleBar.BackColor = _toolbarBg;
        _leftTitleLabel.BackColor = _toolbarBg;
        _leftTitleLabel.ForeColor = _toolbarFg;
        _rightTitleLabel.BackColor = _toolbarBg;
        _rightTitleLabel.ForeColor = _toolbarFg;

        using var boldFont = new Font(_leftTitleLabel.Font, FontStyle.Bold);
        _leftTitleLabel.Font = new Font(_leftTitleLabel.Font, FontStyle.Bold);
        _rightTitleLabel.Font = new Font(_rightTitleLabel.Font, FontStyle.Bold);

        _toolbar.Invalidate();
        _titleBar.Invalidate();
    }

    // ── Layout helpers ───────────────────────────────────────────────

    private void LayoutToolbarButtons()
    {
        using var g = CreateGraphics();
        var font = _prevButton.Font;

        int prevW = TextRenderer.MeasureText(g, _prevButton.Text, font).Width + 24;
        int nextW = TextRenderer.MeasureText(g, _nextButton.Text, font).Width + 24;

        _prevButton.Width = prevW;
        _prevButton.Left = 8;

        _nextButton.Width = nextW;
        _nextButton.Left = _prevButton.Right + 6;

        _diffLabel.Left = _nextButton.Right + 12;
    }

    private void LayoutTitleLabels()
    {
        int splitterPos = _splitContainer.SplitterDistance;
        int splitterWidth = _splitContainer.SplitterWidth;
        int leftOffset = _splitContainer.Left;

        _leftTitleLabel.SetBounds(leftOffset, 0, splitterPos, 26);
        _rightTitleLabel.SetBounds(leftOffset + splitterPos + splitterWidth, 0,
            _titleBar.Width - leftOffset - splitterPos - splitterWidth, 26);
    }

    private void PaintToolbarBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel) return;
        int y = panel.Height - 1;
        using var pen = new Pen(_separatorColor);
        e.Graphics.DrawLine(pen, 0, y, panel.Width, y);
    }

    // ── Navigation ───────────────────────────────────────────────────

    private void NavigateNext()
    {
        if (_result is null || _result.DiffCount == 0) return;

        _currentDiffIndex = (_currentDiffIndex + 1) % _result.DiffCount;
        NavigateToCurrentDiff();
    }

    private void NavigatePrev()
    {
        if (_result is null || _result.DiffCount == 0) return;

        _currentDiffIndex = (_currentDiffIndex - 1 + _result.DiffCount) % _result.DiffCount;
        NavigateToCurrentDiff();
    }

    private void NavigateToCurrentDiff()
    {
        if (_result is null || _currentDiffIndex < 0 || _currentDiffIndex >= _result.DiffSectionStarts.Length)
            return;

        long line = _result.DiffSectionStarts[_currentDiffIndex];
        _suppressScrollSync = true;
        _leftEditor.GoToLine(line);
        _rightEditor.GoToLine(line);
        _suppressScrollSync = false;

        UpdateDiffLabel();
    }

    private void UpdateDiffLabel()
    {
        if (_result is null || _result.DiffCount == 0)
        {
            _diffLabel.Text = "No differences";
            _prevButton.Enabled = false;
            _nextButton.Enabled = false;
        }
        else
        {
            _diffLabel.Text = $"Diff {_currentDiffIndex + 1} of {_result.DiffCount}";
            _prevButton.Enabled = true;
            _nextButton.Enabled = true;
        }
    }

    // ── Synchronized scrolling ───────────────────────────────────────

    private void OnLeftScrollChanged()
    {
        if (_suppressScrollSync) return;
        _suppressScrollSync = true;

        _rightEditor.ScrollMgr.FirstVisibleLine = _leftEditor.ScrollMgr.FirstVisibleLine;
        _rightEditor.ScrollMgr.HorizontalScrollOffset = _leftEditor.ScrollMgr.HorizontalScrollOffset;

        _suppressScrollSync = false;
    }

    private void OnRightScrollChanged()
    {
        if (_suppressScrollSync) return;
        _suppressScrollSync = true;

        _leftEditor.ScrollMgr.FirstVisibleLine = _rightEditor.ScrollMgr.FirstVisibleLine;
        _leftEditor.ScrollMgr.HorizontalScrollOffset = _rightEditor.ScrollMgr.HorizontalScrollOffset;

        _suppressScrollSync = false;
    }

    // ── Synchronized zooming ────────────────────────────────────────

    private void OnLeftZoomChanged(object? sender, EventArgs e)
    {
        if (_suppressZoomSync) return;
        _suppressZoomSync = true;
        _rightEditor.ZoomLevel = _leftEditor.ZoomLevel;
        _suppressZoomSync = false;
    }

    private void OnRightZoomChanged(object? sender, EventArgs e)
    {
        if (_suppressZoomSync) return;
        _suppressZoomSync = true;
        _leftEditor.ZoomLevel = _rightEditor.ZoomLevel;
        _suppressZoomSync = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _leftEditor.ScrollMgr.ScrollChanged -= OnLeftScrollChanged;
            _rightEditor.ScrollMgr.ScrollChanged -= OnRightScrollChanged;
            _leftEditor.ZoomChanged -= OnLeftZoomChanged;
            _rightEditor.ZoomChanged -= OnRightZoomChanged;
            _leftEditor.Dispose();
            _rightEditor.Dispose();
            _splitContainer.Dispose();
            _toolbar.Dispose();
            _titleBar.Dispose();
        }
        base.Dispose(disposing);
    }
}
