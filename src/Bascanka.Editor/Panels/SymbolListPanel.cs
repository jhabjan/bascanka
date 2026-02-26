using Bascanka.Core.Buffer;
using Bascanka.Core.Navigation;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Panels;

/// <summary>
/// A side panel that displays a filterable tree of code symbols
/// (classes, methods, properties, etc.) parsed from the active document.
/// <para>
/// Clicking a symbol navigates the editor to its declaration.  The symbol
/// list auto-refreshes (with debounce) whenever the document changes.
/// Uses the static <see cref="SymbolParser.Parse(PieceTable, string)"/>
/// method from <c>Bascanka.Core.Navigation</c>.
/// </para>
/// </summary>
public class SymbolListPanel : UserControl
{
    // ── Constants ─────────────────────────────────────────────────────
    private const int RefreshDebounceMsec = 500;

    // ── Controls ──────────────────────────────────────────────────────
    private readonly TextBox _filterBox;
    private readonly TreeView _treeView;
    private readonly ImageList _iconList;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // ── State ─────────────────────────────────────────────────────────
    private ITheme? _theme;
    private List<SymbolInfo> _allSymbols = [];
    private bool _refreshPending;
    private PieceTable? _buffer;
    private string _languageId = "csharp";

    // ── Events ────────────────────────────────────────────────────────

    /// <summary>
    /// Raised when the user clicks a symbol to navigate to its location
    /// in the source document.
    /// </summary>
    public event EventHandler<SymbolNavigationEventArgs>? NavigateToSymbol;

    // ── Construction ──────────────────────────────────────────────────

    public SymbolListPanel()
    {
        Dock = DockStyle.Fill;

        // ── Icon list ─────────────────────────────────────────────────
        _iconList = BuildIconList();

        // ── Filter text box ───────────────────────────────────────────
        _filterBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "Filter symbols...",
            BorderStyle = BorderStyle.FixedSingle,
            Height = 24,
        };
        _filterBox.TextChanged += (_, _) => ApplyFilter();

        // ── Tree view ─────────────────────────────────────────────────
        _treeView = new TreeView
        {
            Dock = DockStyle.Fill,
            ImageList = _iconList,
            HideSelection = false,
            ShowLines = true,
            ShowRootLines = true,
            ShowPlusMinus = true,
            FullRowSelect = true,
            BorderStyle = BorderStyle.None,
        };
        _treeView.NodeMouseClick += OnNodeMouseClick;
        _treeView.NodeMouseDoubleClick += OnNodeMouseDoubleClick;
        _treeView.KeyDown += OnTreeViewKeyDown;

        // ── Debounce timer ────────────────────────────────────────────
        _refreshTimer = new System.Windows.Forms.Timer { Interval = RefreshDebounceMsec };
        _refreshTimer.Tick += OnRefreshTimerTick;

        // ── Layout ────────────────────────────────────────────────────
        Controls.Add(_treeView);
        Controls.Add(_filterBox);
    }

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Sets the text buffer and language to use for symbol parsing.
    /// </summary>
    /// <param name="buffer">The <see cref="PieceTable"/> to extract symbols from.</param>
    /// <param name="languageId">
    /// A language identifier (e.g. "csharp", "javascript", "python") passed to
    /// <see cref="SymbolParser.Parse(PieceTable, string)"/>.
    /// </param>
    public void Attach(PieceTable buffer, string languageId)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _languageId = languageId ?? throw new ArgumentNullException(nameof(languageId));
    }

    /// <summary>
    /// The theme used for rendering panel colours.
    /// </summary>
    public ITheme? Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            ApplyTheme();
        }
    }

    /// <summary>
    /// The language identifier used for symbol parsing.
    /// </summary>
    public string LanguageId
    {
        get => _languageId;
        set => _languageId = value ?? "csharp";
    }

    /// <summary>
    /// Schedules a debounced refresh of the symbol list.  Call this method
    /// whenever the document text changes.
    /// </summary>
    public void RequestRefresh()
    {
        _refreshPending = true;
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    /// <summary>
    /// Immediately refreshes the symbol list by parsing the attached buffer.
    /// </summary>
    public void RefreshNow()
    {
        _refreshTimer.Stop();
        _refreshPending = false;

        if (_buffer is null)
        {
            _allSymbols.Clear();
            PopulateTree(_allSymbols);
            return;
        }

        _allSymbols = SymbolParser.Parse(_buffer, _languageId);
        ApplyFilter();
    }

    /// <summary>
    /// Clears all symbols from the panel.
    /// </summary>
    public void Clear()
    {
        _allSymbols.Clear();
        _treeView.Nodes.Clear();
        _filterBox.Clear();
    }

    // ── Refresh timer ─────────────────────────────────────────────────

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();

        if (!_refreshPending) return;
        _refreshPending = false;

        RefreshNow();
    }

    // ── Filtering ─────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        string filter = _filterBox.Text.Trim();

        if (string.IsNullOrEmpty(filter))
        {
            PopulateTree(_allSymbols);
            return;
        }

        List<SymbolInfo> filtered = [.. _allSymbols.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))];

        PopulateTree(filtered);
    }

    // ── Tree population ───────────────────────────────────────────────

    private void PopulateTree(List<SymbolInfo> symbols)
    {
        _treeView.BeginUpdate();
        try
        {
            _treeView.Nodes.Clear();

            // Group symbols by kind for a structured tree.
            var grouped = symbols
                .GroupBy(s => s.Kind)
                .OrderBy(g => g.Key.ToString());

            foreach (var group in grouped)
            {
                string kindLabel = group.Key.ToString();
                int iconIndex = GetIconIndex(group.Key);

                var groupNode = new TreeNode(kindLabel)
                {
                    ImageIndex = iconIndex,
                    SelectedImageIndex = iconIndex,
                };

                foreach (SymbolInfo symbol in group.OrderBy(s => s.LineNumber))
                {
                    string nodeText = $"{symbol.Name}  (line {symbol.LineNumber})";

                    var symbolNode = new TreeNode(nodeText)
                    {
                        Tag = symbol,
                        ImageIndex = iconIndex,
                        SelectedImageIndex = iconIndex,
                    };

                    groupNode.Nodes.Add(symbolNode);
                }

                _treeView.Nodes.Add(groupNode);
            }

            _treeView.ExpandAll();
        }
        finally
        {
            _treeView.EndUpdate();
        }
    }

    // ── Navigation ────────────────────────────────────────────────────

    private void OnNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node?.Tag is SymbolInfo symbol)
        {
            NavigateToSymbol?.Invoke(this, new SymbolNavigationEventArgs(symbol));
        }
    }

    private void OnNodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node?.Tag is SymbolInfo symbol)
        {
            NavigateToSymbol?.Invoke(this, new SymbolNavigationEventArgs(symbol));
        }
    }

    private void OnTreeViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _treeView.SelectedNode?.Tag is SymbolInfo symbol)
        {
            e.SuppressKeyPress = true;
            NavigateToSymbol?.Invoke(this, new SymbolNavigationEventArgs(symbol));
        }
    }

    // ── Icons ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a programmatic icon image list for symbol types.  Each icon
    /// is a small coloured square with a letter abbreviation.
    /// </summary>
    private static ImageList BuildIconList()
    {
        var list = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit,
        };

        // Index 0: Class (C, blue)
        list.Images.Add(CreateSymbolIcon("C", Color.RoyalBlue));
        // Index 1: Method (M, purple)
        list.Images.Add(CreateSymbolIcon("M", Color.MediumPurple));
        // Index 2: Function (f, dark purple)
        list.Images.Add(CreateSymbolIcon("f", Color.DarkOrchid));
        // Index 3: Property (P, teal)
        list.Images.Add(CreateSymbolIcon("P", Color.Teal));
        // Index 4: Interface (I, green)
        list.Images.Add(CreateSymbolIcon("I", Color.SeaGreen));
        // Index 5: Enum (E, orange)
        list.Images.Add(CreateSymbolIcon("E", Color.DarkOrange));
        // Index 6: Struct (S, brown)
        list.Images.Add(CreateSymbolIcon("S", Color.SaddleBrown));
        // Index 7: Other/Unknown (?, gray)
        list.Images.Add(CreateSymbolIcon("?", Color.Gray));

        return list;
    }

    private static Bitmap CreateSymbolIcon(string letter, Color color)
    {
        var bmp = new Bitmap(16, 16);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var bgBrush = new SolidBrush(color);
        g.FillRectangle(bgBrush, 1, 1, 14, 14);

        using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(letter, font, textBrush, new RectangleF(0, 0, 16, 16), sf);

        return bmp;
    }

    /// <summary>
    /// Maps a <see cref="SymbolKind"/> to the corresponding icon index in
    /// <see cref="_iconList"/>.
    /// </summary>
    private static int GetIconIndex(SymbolKind kind) => kind switch
    {
        SymbolKind.Class => 0,
        SymbolKind.Method => 1,
        SymbolKind.Function => 2,
        SymbolKind.Property => 3,
        SymbolKind.Interface => 4,
        SymbolKind.Enum => 5,
        SymbolKind.Struct => 6,
        _ => 7,
    };

    // ── Theme ─────────────────────────────────────────────────────────

    private void ApplyTheme()
    {
        if (_theme is null) return;

        BackColor = _theme.EditorBackground;
        ForeColor = _theme.EditorForeground;
        _treeView.BackColor = _theme.EditorBackground;
        _treeView.ForeColor = _theme.EditorForeground;
        _filterBox.BackColor = _theme.EditorBackground;
        _filterBox.ForeColor = _theme.EditorForeground;

        Invalidate(true);
    }

    // ── Disposal ──────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _iconList.Dispose();
        }
        base.Dispose(disposing);
    }
}
