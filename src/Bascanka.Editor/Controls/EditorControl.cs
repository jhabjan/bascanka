using System.Drawing;
using System.Text.RegularExpressions;
using Bascanka.Core.Buffer;
using Bascanka.Core.Commands;
using Bascanka.Core.Diff;
using Bascanka.Core.Search;
using Bascanka.Core.Syntax;
using Bascanka.Editor.HexEditor;
using Bascanka.Editor.Highlighting;
using Bascanka.Editor.Macros;
using Bascanka.Editor.Panels;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Controls;

/// <summary>
/// The main composite editor control that assembles an <see cref="EditorSurface"/>,
/// <see cref="GutterRenderer"/>, scrollbars, and all supporting managers into a
/// complete text-editing user control.
/// <para>
/// Layout:  [Gutter][EditorSurface][VScrollBar]
///                                  [HScrollBar]
/// </para>
/// </summary>
public sealed class EditorControl : UserControl
{
    // ── Child controls ─────────────────────────────────────────────────
    private readonly EditorSurface _surface;
    private readonly BufferedPanel _gutterPanel;
    private readonly VScrollBar _vScrollBar;
    private readonly HScrollBar _hScrollBar;

    // ── Managers ───────────────────────────────────────────────────────
    private readonly CaretManager _caretManager;
    private readonly SelectionManager _selectionManager;
    private readonly ScrollManager _scrollManager;
    private readonly InputHandler _inputHandler;
    private readonly FoldingManager _foldingManager;
    private readonly GutterRenderer _gutterRenderer;
    private readonly CommandHistory _commandHistory;

    // ── Syntax ─────────────────────────────────────────────────────────
    private readonly TokenCache _tokenCache;
    private ILexer? _lexer;
    private string _language = string.Empty;
    private ITheme _theme;

    // ── Document ───────────────────────────────────────────────────────
    private PieceTable _document;
    private bool _readOnly;
    private string? _filePath;
    private long _fileSizeBytes;

    // ── Custom highlighting ─────────────────────────────────────────────
    private CustomHighlightMatcher? _customHighlightMatcher;
    private string? _customProfileName;
    private List<BlockRegion>? _customBlockRegions;

    // ── Find/Replace panel ─────────────────────────────────────────────
    private FindReplacePanel? _findPanel;

    // ── Hex editor split panel ──────────────────────────────────────────
    private SplitContainer? _hexSplit;
    private HexEditorControl? _hexEditor;
    private System.Windows.Forms.Timer? _hexSyncTimer;
    private bool _hexPanelVisible;
    private bool _isBinaryMode;

    // ── Macros ──────────────────────────────────────────────────────────
    private readonly MacroRecorder _macroRecorder = new();
    private readonly MacroPlayer _macroPlayer = new();
    private static Macro? s_lastRecordedMacro;
    private static readonly MacroManager s_macroManager = new();

    // ── Context menu ────────────────────────────────────────────────────
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _ctxUndo;
    private readonly ToolStripMenuItem _ctxRedo;
    private readonly ToolStripMenuItem _ctxCut;
    private readonly ToolStripMenuItem _ctxCopy;
    private readonly ToolStripMenuItem _ctxPaste;
    private readonly ToolStripMenuItem _ctxDelete;
    private readonly ToolStripMenuItem _ctxSelectAll;

    // ── Configurable defaults (set from SettingsManager at startup) ────
    public static string DefaultFontFamily { get; set; } = "Consolas";
    public static float DefaultFontSize { get; set; } = 11f;
    public static int DefaultTabWidth { get; set; } = 4;
    public static int DefaultScrollSpeed { get; set; } = 3;
    public static int DefaultCaretBlinkRate { get; set; } = 500;
    public static long FoldingMaxFileSize { get; set; } = 50_000_000;
    public static bool DefaultAutoIndent { get; set; } = true;
    public static int DefaultCaretScrollBuffer { get; set; } = 4;
    public static int DefaultTextLeftPadding { get; set; } = 6;
    public static int DefaultLineSpacing { get; set; } = 2;
    public static float DefaultMinZoomFontSize { get; set; } = 6f;
    public static int DefaultWhitespaceOpacity { get; set; } = 100;
    public static int DefaultFoldIndicatorOpacity { get; set; } = 60;
    public static int DefaultGutterPaddingLeft { get; set; } = 8;
    public static int DefaultGutterPaddingRight { get; set; } = 12;
    public static int DefaultFoldButtonSize { get; set; } = 10;
    public static int DefaultBookmarkSize { get; set; } = 8;
    public static int DefaultSearchDebounce { get; set; } = 300;

    // ── Extended state ──────────────────────────────────────────────────
    private Bascanka.Core.Encoding.EncodingManager? _encodingManager;
    private bool _wordWrap;
    private bool _showWhitespace;
    private bool _showLineNumbers = true;
    private string _lineEnding = "CRLF";
    private int _zoomLevel;

    // ── Events ─────────────────────────────────────────────────────────

    /// <summary>Raised when the document text changes.</summary>
    public new event EventHandler? TextChanged;

    /// <summary>Raised when the dirty (modified) state changes.</summary>
    public event EventHandler? DirtyChanged;

    /// <summary>Raised when the caret moves.</summary>
    public event EventHandler<long>? CaretMoved;

    /// <summary>Raised when the <see cref="Document"/> property changes.</summary>
    public event EventHandler? DocumentChanged;

    /// <summary>Raised when document content changes (alias for TextChanged).</summary>
    public event EventHandler? ContentChanged;

    /// <summary>Raised when the caret position changes.</summary>
    public event EventHandler? CaretPositionChanged;

    /// <summary>Raised when the selection changes.</summary>
    public new event EventHandler? SelectionChanged;

    /// <summary>Raised when the zoom level changes.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Raised when the hex panel visibility changes.</summary>
    public event EventHandler? HexPanelVisibilityChanged;

    /// <summary>Raised when "Find All" is clicked, carrying the search pattern and results.</summary>
    public event EventHandler<FindNextRequestEventArgs>? FindNextRequested;
    public event EventHandler<FindAllEventArgs>? FindAllRequested;

    /// <summary>Raised when "Find All in Tabs" is clicked, carrying the search options for multi-tab search.</summary>
    public event EventHandler<FindAllInTabsEventArgs>? FindAllInTabsRequested;

    /// <summary>Raised when insert/overwrite mode is toggled via the Insert key.</summary>
    public event EventHandler? InsertModeChanged;

    // ────────────────────────────────────────────────────────────────────
    //  Construction
    // ────────────────────────────────────────────────────────────────────

    public EditorControl()
    {
        SuspendLayout();

        // Initialise document and history.
        _document = new PieceTable(string.Empty);
        _commandHistory = new CommandHistory();
        _tokenCache = new TokenCache();
        _theme = new DarkTheme();

        // Create managers.
        _caretManager = new CaretManager { Document = _document };
        _caretManager.ColumnToExpandedColumn = (docLine, col) =>
        {
            string text = _document.GetLine(docLine);
            return ExpandedLength(text, Math.Min(col, text.Length));
        };
        _selectionManager = new SelectionManager { Document = _document };
        _scrollManager = new ScrollManager();
        _foldingManager = new FoldingManager();
        _caretManager.Folding = _foldingManager;
        _gutterRenderer = new GutterRenderer();

        _inputHandler = new InputHandler
        {
            Document = _document,
            Caret = _caretManager,
            Selection = _selectionManager,
            History = _commandHistory,
            Folding = _foldingManager,
            MacroRecorder = _macroRecorder,
        };

        // Create child controls.
        _vScrollBar = new VScrollBar { Dock = DockStyle.Right };
        _hScrollBar = new HScrollBar { Dock = DockStyle.Bottom };

        _gutterPanel = new BufferedPanel
        {
            Dock = DockStyle.Left,
            Width = _gutterRenderer.Width,
            BackColor = _theme.GutterBackground,
        };
        _gutterPanel.Paint += OnGutterPaint;
        _gutterPanel.MouseDown += OnGutterMouseDown;
        _gutterPanel.MouseMove += OnGutterMouseMove;

        _surface = new EditorSurface
        {
            Dock = DockStyle.Fill,
            Document = _document,
            Caret = _caretManager,
            Selection = _selectionManager,
            Scroll = _scrollManager,
            Folding = _foldingManager,
            Tokens = _tokenCache,
            InputHandler = _inputHandler,
            Theme = _theme,
        };

        // Add controls in correct order for docking.
        Controls.Add(_surface);
        Controls.Add(_gutterPanel);
        Controls.Add(_vScrollBar);
        Controls.Add(_hScrollBar);

        // Wire up scrollbars.
        _scrollManager.AttachScrollBars(_vScrollBar, _hScrollBar);

        // Wire up events.
        _scrollManager.ScrollChanged += OnScrollChanged;
        _caretManager.CaretMoved += OnCaretMoved;
        _caretManager.BlinkStateChanged += OnBlinkStateChanged;
        _selectionManager.SelectionChanged += OnSelectionChanged;
        _foldingManager.FoldingChanged += OnFoldingChanged;
        _inputHandler.TextModified += OnTextModified;
        _inputHandler.InsertModeChanged += () => InsertModeChanged?.Invoke(this, EventArgs.Empty);
        _commandHistory.SavePointChanged += OnSavePointChanged;
        _document.TextChanged += OnDocumentTextChanged;

        _surface.Resize += (_, _) =>
        {
            UpdateScrollBars();
            RetokenizeAllVisible();
            if (_wordWrap)
                _gutterPanel.Invalidate();
        };

        _surface.ZoomRequested += delta =>
        {
            if (delta > 0) ZoomIn();
            else ZoomOut();
        };

        // Build context menu.
        _ctxUndo = new ToolStripMenuItem("Undo", null, (_, _) => Undo()) { ShortcutKeyDisplayString = "Ctrl+Z" };
        _ctxRedo = new ToolStripMenuItem("Redo", null, (_, _) => Redo()) { ShortcutKeyDisplayString = "Ctrl+Y" };
        _ctxCut = new ToolStripMenuItem("Cut", null, (_, _) => Cut()) { ShortcutKeyDisplayString = "Ctrl+X" };
        _ctxCopy = new ToolStripMenuItem("Copy", null, (_, _) => Copy()) { ShortcutKeyDisplayString = "Ctrl+C" };
        _ctxPaste = new ToolStripMenuItem("Paste", null, (_, _) => Paste()) { ShortcutKeyDisplayString = "Ctrl+V" };
        _ctxDelete = new ToolStripMenuItem("Delete", null, (_, _) => DeleteSelection());
        _ctxSelectAll = new ToolStripMenuItem("Select All", null, (_, _) => SelectAll()) { ShortcutKeyDisplayString = "Ctrl+A" };

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.AddRange([
            _ctxUndo, _ctxRedo,
            new ToolStripSeparator(),
            _ctxCut, _ctxCopy, _ctxPaste, _ctxDelete,
            new ToolStripSeparator(),
            _ctxSelectAll,
        ]);
        _contextMenu.Opening += OnContextMenuOpening;
        _surface.ContextMenuStrip = _contextMenu;

        // Apply theme.
        ApplyTheme(_theme);

        ResumeLayout(true);

        // Start caret blink.
        _caretManager.StartBlink();
    }

    /// <summary>
    /// Creates an editor control with the given document buffer.
    /// </summary>
    public EditorControl(PieceTable buffer) : this()
    {
        if (buffer is not null)
            Document = buffer;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Properties
    // ────────────────────────────────────────────────────────────────────

    /// <summary>The underlying document buffer.</summary>
    public PieceTable Document
    {
        get => _document;
        set
        {
            if (value is null) throw new ArgumentNullException(nameof(value));

            // Unwire and dispose old document.
            PieceTable oldDocument = _document;
            oldDocument.TextChanged -= OnDocumentTextChanged;

            _document = value;

            oldDocument.Dispose();
            _document.TextChanged += OnDocumentTextChanged;

            // Update all managers.
            _caretManager.Document = _document;
            _selectionManager.Document = _document;
            _inputHandler.Document = _document;
            _surface.Document = _document;

            _tokenCache.Clear();
            _commandHistory.Clear();

            UpdateScrollBars();
            _caretManager.MoveTo(0);

            // Re-attach find panel to the new buffer.
            if (_findPanel is not null && _findPanel.Visible)
                _findPanel.Attach(this, _document);

            DocumentChanged?.Invoke(this, EventArgs.Empty);
            Invalidate(true);
        }
    }

    /// <summary>
    /// Zero-based character offset of the caret within the document.
    /// Provided for backward compatibility with the original placeholder.
    /// </summary>
    public long CaretOffset
    {
        get => _caretManager.Offset;
        set => _caretManager.MoveTo(value);
    }

    /// <summary>
    /// The one-based line number where the caret currently resides.
    /// </summary>
    public long CaretLine => _caretManager.Line + 1;

    /// <summary>
    /// The language identifier for syntax highlighting (e.g. "csharp", "javascript").
    /// Setting this looks up the corresponding <see cref="ILexer"/> in the
    /// <see cref="LexerRegistry"/>.
    /// </summary>
    public string Language
    {
        get => _language;
        set
        {
            _language = value ?? string.Empty;
            _lexer = LexerRegistry.Instance.GetLexerById(_language);
            _surface.Lexer = _lexer;
            _tokenCache.Clear();
            RetokenizeAllVisible();
            DetectFoldingRegions();
            Invalidate(true);
        }
    }

    /// <summary>The current colour theme.</summary>
    public ITheme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? throw new ArgumentNullException(nameof(value));
            ApplyTheme(_theme);
            Invalidate(true);
        }
    }

    /// <summary>Whether the editor is in read-only mode.</summary>
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            _readOnly = value;
            _inputHandler.ReadOnly = value;
        }
    }

    /// <summary>Whether the document has been modified since the last save.</summary>
    public bool IsDirty => _commandHistory.IsDirty;

    /// <summary>The <see cref="CommandHistory"/> instance for undo/redo.</summary>
    public CommandHistory History => _commandHistory;

    /// <summary>Current caret line (zero-based).</summary>
    public long CurrentLine => _caretManager.Line;

    /// <summary>Current caret column (zero-based).</summary>
    public long CurrentColumn => _caretManager.Column;

    /// <summary>Total number of lines in the document.</summary>
    public long TotalLines => _document.LineCount;

    /// <summary>The file path associated with this document, or null if untitled.</summary>
    public string? FilePath => _filePath;

    /// <summary>The file size in bytes on disk. Updated on load/save.</summary>
    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        set => _fileSizeBytes = value;
    }

    /// <summary>The caret manager.</summary>
    public CaretManager CaretMgr => _caretManager;

    /// <summary>The selection manager.</summary>
    public SelectionManager SelectionMgr => _selectionManager;

    /// <summary>The scroll manager.</summary>
    public ScrollManager ScrollMgr => _scrollManager;

    /// <summary>The folding manager.</summary>
    public FoldingManager FoldingMgr => _foldingManager;

    /// <summary>The gutter renderer.</summary>
    public GutterRenderer Gutter => _gutterRenderer;

    /// <summary>The find/replace panel, or null if not yet shown.</summary>
    public FindReplacePanel? FindPanel => _findPanel;

    /// <summary>The encoding manager for this document.</summary>
    public Bascanka.Core.Encoding.EncodingManager? EncodingManager
    {
        get => _encodingManager;
        set => _encodingManager = value;
    }

    /// <summary>Whether word wrap is enabled.</summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            _wordWrap = value;
            _surface.WordWrap = value;

            // When word-wrap is active, the caret needs to compute its visible
            // row in wrap-row space for correct scroll-into-view behavior.
            if (value)
            {
                _caretManager.LineColumnToVisibleRow = (docLine, col) =>
                {
                    string text = _document.GetLine(docLine);
                    int expandedCol = ExpandedLength(text, (int)Math.Min(col, text.Length));
                    int wrapCols = _surface.WrapColumns;
                    int wrapRow = wrapCols > 0 ? expandedCol / wrapCols : 0;
                    return _surface.DocumentLineToWrapRow(docLine, wrapRow);
                };
            }
            else
            {
                _caretManager.LineColumnToVisibleRow = null;
            }

            UpdateScrollBars();
            _surface.Invalidate();
            _gutterPanel.Invalidate();
        }
    }

    /// <summary>Whether whitespace characters are rendered.</summary>
    public bool ShowWhitespace
    {
        get => _showWhitespace;
        set
        {
            _showWhitespace = value;
            _surface.ShowWhitespace = value;
            _surface.Invalidate();
        }
    }

    /// <summary>Whether line numbers are shown in the gutter.</summary>
    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            _showLineNumbers = value;
            _gutterPanel.Visible = value;
            Invalidate(true);
        }
    }

    /// <summary>Whether the editor is in read-only mode (alias for ReadOnly).</summary>
    public bool IsReadOnly
    {
        get => _readOnly;
        set => ReadOnly = value;
    }

    /// <summary>Whether the editor is in insert mode (vs. overwrite).</summary>
    public bool InsertMode
    {
        get => _inputHandler.InsertMode;
        set { /* InsertMode is toggled via Insert key in InputHandler */ }
    }

    /// <summary>Current zoom level as a percentage (100 = default).</summary>
    public int ZoomPercentage => (int)Math.Round(Math.Max(6, 11 + _zoomLevel) / 11.0 * 100);

    /// <summary>Gets or sets the raw zoom level (0 = default, positive = zoomed in, negative = zoomed out).</summary>
    public int ZoomLevel
    {
        get => _zoomLevel;
        set { _zoomLevel = value; ApplyZoom(); }
    }

    /// <summary>Whether the hex editor split panel is currently visible.</summary>
    public bool IsHexPanelVisible
    {
        get => _hexPanelVisible;
        set
        {
            if (_isBinaryMode) return; // Binary mode locks hex-only view.
            if (_hexPanelVisible == value) return;
            _hexPanelVisible = value;
            if (value) ShowHexPanel(); else HideHexPanel();
            HexPanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether this editor is showing a binary file in hex-only mode.</summary>
    public bool IsBinaryMode => _isBinaryMode;

    /// <summary>Per-line diff metadata for diff view mode.</summary>
    public DiffLine[]? DiffLineMarkers
    {
        get => _surface.DiffLineMarkers;
        set
        {
            _surface.DiffLineMarkers = value;
            _gutterRenderer.DiffLineMarkers = value;
            Invalidate(true);
        }
    }

    /// <summary>
    /// Opens raw bytes in hex-only mode: the text editor is hidden and only
    /// the hex editor is shown. Used for binary files (exe, images, etc.).
    /// </summary>
    public void ShowHexOnly(byte[] data)
    {
        _isBinaryMode = true;
        _hexPanelVisible = true;

        // Create hex editor if not yet created.
        if (_hexEditor is null)
        {
            _hexEditor = new HexEditorControl
            {
                Dock = DockStyle.Fill,
                Theme = _theme,
                IsReadOnly = _readOnly,
            };
        }

        _hexEditor.Data = data;

        // Remove all existing controls and show only the hex editor.
        SuspendLayout();
        Controls.Clear();
        Controls.Add(_hexEditor);
        ResumeLayout(true);
    }

    /// <summary>The current line ending mode (CRLF, LF, CR).</summary>
    public string LineEnding
    {
        get => _lineEnding;
        set => _lineEnding = value ?? "CRLF";
    }

    /// <summary>Length of the current text selection in characters.</summary>
    public int SelectionLength =>
        _selectionManager.IsColumnMode
            ? (_selectionManager.HasColumnSelection ? 1 : 0)
            : (_selectionManager.HasSelection
                ? (int)Math.Min(_selectionManager.SelectionEnd - _selectionManager.SelectionStart, int.MaxValue)
                : 0);

    /// <summary>The currently active lexer, or null for plain text.</summary>
    public ILexer? CurrentLexer => _lexer;

    /// <summary>The name of the active custom highlight profile, or null if none.</summary>
    public string? CustomProfileName => _customProfileName;

    // ────────────────────────────────────────────────────────────────────
    //  Public methods
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the entire document content with the given text.
    /// </summary>
    public void LoadText(string text)
    {
        Document = new PieceTable(text ?? string.Empty);
        DetectLanguageFromContent();
        RetokenizeAllVisible();
    }

    /// <summary>
    /// Loads a file from disk into the editor.
    /// </summary>
    public void LoadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _fileSizeBytes = new FileInfo(path).Length;
        string text = File.ReadAllText(path);
        // Normalize line endings internally to \n.
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        Document = new PieceTable(text);
        _filePath = path;

        // Detect language from file extension.
        string ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext))
        {
            ILexer? lexer = LexerRegistry.Instance.GetLexerByExtension(ext);
            if (lexer is not null)
            {
                _language = lexer.LanguageId;
                _lexer = lexer;
                _surface.Lexer = _lexer;
            }
        }

        _commandHistory.SetSavePoint();
        RetokenizeAllVisible();
        DetectFoldingRegions();
    }

    /// <summary>
    /// Saves the document to the specified file path.
    /// </summary>
    public void SaveFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string content = _document.ToString();
        File.WriteAllText(path, content);
        _filePath = path;
        _fileSizeBytes = new FileInfo(path).Length;
        _commandHistory.SetSavePoint();
    }

    /// <summary>
    /// Navigates the caret to the specified line (zero-based).
    /// </summary>
    public void GoToLine(long line)
    {
        line = Math.Clamp(line, 0, _document.LineCount - 1);
        _foldingManager.EnsureLineVisible(line);
        _caretManager.MoveToLineColumn(line, 0);
        long visLine = _foldingManager.DocumentLineToVisibleLine(line);
        _scrollManager.EnsureLineVisible(visLine >= 0 ? visLine : line);
        _selectionManager.ClearSelection();
        Invalidate(true);
    }

    /// <summary>
    /// Scrolls the view so that the specified line is visible.
    /// </summary>
    /// <param name="lineNumber">One-based line number to scroll to.</param>
    public void ScrollToLine(long lineNumber)
    {
        long zeroBasedLine = Math.Clamp(lineNumber - 1, 0, _document.LineCount - 1);
        GoToLine(zeroBasedLine);
    }

    /// <summary>
    /// Selects a range of text in the editor.
    /// </summary>
    /// <param name="offset">Zero-based start offset.</param>
    /// <param name="length">Number of characters to select.</param>
    public void Select(long offset, int length)
    {
        if (_document.Length == 0) return;
        offset = Math.Clamp(offset, 0, _document.Length);
        long end = Math.Clamp(offset + length, 0, _document.Length);

        _selectionManager.StartSelection(offset);
        _selectionManager.ExtendSelection(end);
        _caretManager.MoveTo(end);
        Invalidate(true);
    }

    /// <summary>
    /// Finds and selects the first occurrence of the given text starting
    /// from the current caret position.
    /// </summary>
    /// <returns><see langword="true"/> if found; otherwise <see langword="false"/>.</returns>
    public bool Find(string text)
    {
        if (string.IsNullOrEmpty(text) || _document.Length == 0) return false;

        long startOffset = _caretManager.Offset;
        long docLen = _document.Length;

        // Search from caret to end of document.
        for (long i = startOffset; i <= docLen - text.Length; i++)
        {
            if (MatchAt(i, text))
            {
                SelectAndScrollTo(i, text.Length);
                return true;
            }
        }

        // Wrap around: search from beginning to caret.
        for (long i = 0; i < startOffset && i <= docLen - text.Length; i++)
        {
            if (MatchAt(i, text))
            {
                SelectAndScrollTo(i, text.Length);
                return true;
            }
        }

        return false;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Find helpers
    // ────────────────────────────────────────────────────────────────────

    private bool MatchAt(long offset, string text)
    {
        for (int j = 0; j < text.Length; j++)
        {
            if (_document.GetCharAt(offset + j) != text[j])
                return false;
        }
        return true;
    }

    private void SelectAndScrollTo(long offset, int length)
    {
        _selectionManager.ClearSelection();
        // Auto-expand any collapsed region hiding the target.
        var (targetLine, _) = _document.OffsetToLineColumn(offset);
        _foldingManager.EnsureLineVisible(targetLine);
        _selectionManager.StartSelection(offset);
        _selectionManager.ExtendSelection(offset + length);
        _caretManager.MoveTo(offset + length);
        long visLine = _foldingManager.DocumentLineToVisibleLine(_caretManager.Line);
        _scrollManager.EnsureLineVisible(visLine >= 0 ? visLine : _caretManager.Line);
        Invalidate(true);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Extended API (used by MainForm / App layer)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Returns the entire document text.</summary>
    public string GetAllText() => _document.ToString();

    /// <summary>Returns the length of the document in characters.</summary>
    public long GetBufferLength() => _document.Length;

    /// <summary>Sets the syntax highlighting lexer.</summary>
    public void SetLexer(ILexer? lexer)
    {
        // Clear custom highlighting when switching to a built-in lexer.
        _customHighlightMatcher = null;
        _customProfileName = null;
        _customBlockRegions = null;
        _surface.CustomHighlightMatcher = null;
        _surface.CustomBlockRegions = null;

        _lexer = lexer;
        _language = lexer?.LanguageId ?? string.Empty;
        _surface.Lexer = _lexer;
        _tokenCache.Clear();
        RetokenizeAllVisible();
        DetectFoldingRegions();
        Invalidate(true);
    }

    /// <summary>
    /// Activates custom regex-based highlighting from a profile, replacing
    /// the built-in lexer. Pass null to deactivate.
    /// </summary>
    public void SetCustomHighlighting(CustomHighlightProfile? profile)
    {
        if (profile is not null)
        {
            _customHighlightMatcher = new CustomHighlightMatcher(profile);
            _customProfileName = profile.Name;
            _lexer = null;
            _language = string.Empty;
            _surface.Lexer = null;
            _tokenCache.Clear();

            // Scan block regions if the matcher has block rules and the document is small enough.
            // Block scanning uses efficient batch line reads — allow up to 50 M chars
            // (vs 10 M for language fold detection which uses per-line GetLine calls).
            if (_customHighlightMatcher.HasBlockRules && _document.Length < FoldingMaxFileSize)
            {
                _customBlockRegions = _customHighlightMatcher.ScanBlocks(
                    (start, count) =>
                    {
                        var data = _document.GetLineRange(start, count);
                        var texts = new string[data.Length];
                        for (int i = 0; i < data.Length; i++)
                            texts[i] = data[i].Text;
                        return texts;
                    },
                    _document.LineCount);
                _surface.CustomBlockRegions = _customBlockRegions;

                // Set foldable block regions.
                var foldRegions = _customBlockRegions
                    .Where(b => b.Foldable)
                    .Select(b => new FoldRegion(b.StartLine, b.EndLine));
                _foldingManager.SetRegions(foldRegions);
            }
            else
            {
                _customBlockRegions = null;
                _surface.CustomBlockRegions = null;
            }
        }
        else
        {
            _customHighlightMatcher = null;
            _customProfileName = null;
            _customBlockRegions = null;
            _surface.CustomBlockRegions = null;
            _foldingManager.SetRegions(Enumerable.Empty<FoldRegion>());
        }
        _surface.CustomHighlightMatcher = _customHighlightMatcher;
        Invalidate(true);
    }

    /// <summary>Sets the line ending mode (CRLF, LF, CR).</summary>
    public void SetLineEnding(string ending) => _lineEnding = ending ?? "CRLF";

    /// <summary>Shows the find/replace panel.</summary>
    public void ShowFindPanel(bool replaceMode)
    {
        if (_findPanel is null)
        {
            _findPanel = new FindReplacePanel();
            _findPanel.Theme = _theme;

            _findPanel.NavigateToMatch += OnFindNavigateToMatch;
            _findPanel.PanelClosed += OnFindPanelClosed;
            _findPanel.MatchesHighlighted += OnFindMatchesHighlighted;
            _findPanel.FindNextRequested += OnFindNextRequested;
            _findPanel.FindAllRequested += OnFindAllRequested;
            _findPanel.FindAllInTabsRequested += OnFindAllInTabsRequested;

            Controls.Add(_findPanel);
            _findPanel.BringToFront();
        }

        _findPanel.Attach(this, _document);
        _findPanel.ReplaceVisible = replaceMode;
        _findPanel.Visible = true;
        _findPanel.BringToFront();

        // Pre-populate with selected text if any.
        if (_selectionManager.HasSelection)
        {
            long start = _selectionManager.SelectionStart;
            long end = _selectionManager.SelectionEnd;
            int len = (int)Math.Min(end - start, 1000);
            if (len > 0)
            {
                string selected = _document.GetText(start, len);
                // Only use single-line selections as search text.
                if (!selected.Contains('\n') && !selected.Contains('\r'))
                    _findPanel.SetSearchText(selected);
                else
                    _findPanel.SetSearchText(string.Empty);
            }
        }
        else
        {
            _findPanel.SetSearchText(string.Empty);
        }
    }

    /// <summary>Hides the find/replace panel and returns focus to the editor.</summary>
    public void HideFindPanel()
    {
        if (_findPanel is not null)
        {
            _findPanel.Visible = false;
            _surface.Focus();
        }
    }

    private void OnFindNavigateToMatch(object? sender, SearchResult match)
    {
        SelectAndScrollTo(match.Offset, match.Length);
    }

    private void OnFindPanelClosed(object? sender, EventArgs e)
    {
        // Clear highlights when panel closes.
        _surface.SearchHighlightPattern = null;
        _surface.Invalidate();
        HideFindPanel();
    }

    private void OnFindNextRequested(object? sender, FindNextRequestEventArgs e)
    {
        FindNextRequested?.Invoke(this, e);
    }

    private void OnFindAllRequested(object? sender, SearchOptions options)
    {
        string pattern = _findPanel?.SearchPattern ?? string.Empty;
        FindAllRequested?.Invoke(this, new FindAllEventArgs(pattern, options));
    }

    private void OnFindAllInTabsRequested(object? sender, SearchOptions options)
    {
        FindAllInTabsRequested?.Invoke(this, new FindAllInTabsEventArgs(options));
    }

    /// <summary>
    /// Delivers completed Find All results back to the find panel and surface.
    /// Called by MainForm after the background search finishes.
    /// </summary>
    /// <summary>
    /// Delivers a completed Find Next/Previous result back to the find panel.
    /// Called by MainForm after the background search finishes.
    /// </summary>
    public void SetFindNextResult(SearchResult? result)
    {
        _findPanel?.DeliverFindNextResult(result);
    }

    public void SetFindAllResults(List<SearchResult> results)
    {
        // SetFindAllResults fires MatchesHighlighted which sets the surface pattern.
        _findPanel?.SetFindAllResults(results);
        _surface.Invalidate();
    }

    private void OnFindMatchesHighlighted(object? sender, Regex? pattern)
    {
        // Set the highlight pattern for per-line rendering.
        _surface.SearchHighlightPattern = pattern;
        _surface.Invalidate();
    }

    /// <summary>Shows the Go to Line dialog.</summary>
    public void ShowGoToLineDialog()
    {
        using var dialog = new Form
        {
            Text = "Go to Line",
            Size = new Size(300, 130),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };

        var label = new Label { Text = $"Line number (1 - {TotalLines}):", Left = 12, Top = 12, Width = 260 };
        var textBox = new TextBox { Left = 12, Top = 36, Width = 260, Text = (CurrentLine + 1).ToString() };
        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 116, Top = 66, Width = 75 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 197, Top = 66, Width = 75 };

        dialog.Controls.AddRange([label, textBox, btnOk, btnCancel]);
        dialog.AcceptButton = btnOk;
        dialog.CancelButton = btnCancel;
        textBox.SelectAll();

        if (dialog.ShowDialog(FindForm()) == DialogResult.OK &&
            long.TryParse(textBox.Text, out long lineNum))
        {
            GoToLine(lineNum - 1);
        }
    }

    /// <summary>Increases the font size.</summary>
    public void ZoomIn()
    {
        _zoomLevel++;
        ApplyZoom();
    }

    /// <summary>Decreases the font size.</summary>
    public void ZoomOut()
    {
        _zoomLevel--;
        ApplyZoom();
    }

    /// <summary>Resets the font size to default.</summary>
    public void ResetZoom()
    {
        _zoomLevel = 0;
        ApplyZoom();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Embedded hex editor panel
    // ────────────────────────────────────────────────────────────────────

    private void ShowHexPanel()
    {
        if (_hexSplit is not null)
        {
            _hexSplit.Panel2Collapsed = false;
            SyncHexEditorContent();
            return;
        }

        _hexEditor = new HexEditorControl
        {
            Dock = DockStyle.Fill,
            Theme = _theme,
            IsReadOnly = true,
        };

        _hexSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.None,
            BorderStyle = BorderStyle.None,
        };

        SuspendLayout();
        _hexSplit.SuspendLayout();

        var controlsToMove = new List<Control>();
        foreach (Control c in Controls)
        {
            if (c != _findPanel)
                controlsToMove.Add(c);
        }

        foreach (var c in controlsToMove)
        {
            Controls.Remove(c);
            _hexSplit.Panel1.Controls.Add(c);
        }

        _hexSplit.Panel2.Controls.Add(_hexEditor);
        Controls.Add(_hexSplit);

        if (_findPanel is not null)
            _findPanel.BringToFront();

        _hexSplit.Panel1MinSize = 100;
        _hexSplit.Panel2MinSize = 100;

        _hexSplit.ResumeLayout(true);
        ResumeLayout(true);
        PerformLayout();

        // Set splitter distance after layout so Width is valid.
        try
        {
            int splitWidth = _hexSplit.Width - _hexSplit.SplitterWidth;
            int desired = (int)(splitWidth * 0.5);
            _hexSplit.SplitterDistance = Math.Clamp(desired, _hexSplit.Panel1MinSize,
                Math.Max(_hexSplit.Panel1MinSize, splitWidth - _hexSplit.Panel2MinSize));
        }
        catch (InvalidOperationException) { /* control not yet sized */ }

        _hexSyncTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _hexSyncTimer.Tick += (_, _) => { _hexSyncTimer.Stop(); SyncHexEditorContent(); };

        ContentChanged += OnContentChangedForHexSync;

        SyncHexEditorContent();
    }

    private void HideHexPanel()
    {
        if (_hexSplit is null) return;
        _hexSplit.Panel2Collapsed = true;
    }

    private void OnContentChangedForHexSync(object? sender, EventArgs e)
    {
        if (_hexSyncTimer is not null && _hexPanelVisible)
        {
            _hexSyncTimer.Stop();
            _hexSyncTimer.Start();
        }
    }

    private void SyncHexEditorContent()
    {
        if (_hexEditor is null || !_hexPanelVisible) return;

        try
        {
            string text = _document.ToString();
            var encoding = _encodingManager?.CurrentEncoding
                ?? new System.Text.UTF8Encoding(false);
            byte[] bytes = encoding.GetBytes(text);
            _hexEditor.Data = bytes;
        }
        catch
        {
            // Silently ignore encoding errors during sync.
        }
    }

    /// <summary>Whether a macro is currently being recorded.</summary>
    public bool IsRecordingMacro => _macroRecorder.IsRecording;

    /// <summary>Starts recording a macro.</summary>
    public void StartMacroRecording()
    {
        if (_macroRecorder.IsRecording) return;
        _macroRecorder.StartRecording();
    }

    /// <summary>Stops recording a macro and saves the result.</summary>
    public void StopMacroRecording()
    {
        if (!_macroRecorder.IsRecording) return;
        Macro macro = _macroRecorder.StopRecording();
        if (macro.Actions.Count > 0)
        {
            s_lastRecordedMacro = macro;
            s_macroManager.Add(macro);
        }
    }

    /// <summary>Plays the last recorded macro.</summary>
    public async void PlayMacro()
    {
        if (s_lastRecordedMacro is null || _macroRecorder.IsRecording || _macroPlayer.IsPlaying)
            return;

        _macroPlayer.ActionExecuted += OnMacroActionExecuted;
        _macroPlayer.PlaybackFinished += OnMacroPlaybackFinished;

        try
        {
            await _macroPlayer.PlayAsync(s_lastRecordedMacro, _document, _caretManager.Offset);
        }
        catch
        {
            // Playback errors are reported via the PlaybackError event.
        }
    }

    /// <summary>Shows the macro manager dialog.</summary>
    public void ShowMacroManager()
    {
        using var dialog = new Form
        {
            Text = "Macro Manager",
            Size = new Size(500, 400),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = _theme.EditorBackground,
            ForeColor = _theme.EditorForeground,
        };

        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            BackColor = _theme.EditorBackground,
            ForeColor = _theme.EditorForeground,
            BorderStyle = BorderStyle.None,
            Font = new Font(Font.FontFamily, 10f),
            ItemHeight = 24,
        };

        RefreshMacroList(listBox);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 8, 8, 8),
            BackColor = _theme.EditorBackground,
        };

        Button MakeDialogButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 100,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = _theme.EditorBackground,
                ForeColor = _theme.EditorForeground,
                Font = new Font(Font.FontFamily, 9f),
                Margin = new Padding(4, 0, 0, 0),
            };
        }

        var closeBtn = MakeDialogButton("Close");
        closeBtn.Click += (_, _) => dialog.Close();

        var playBtn = MakeDialogButton("Play");
        playBtn.Click += (_, _) =>
        {
            int idx = listBox.SelectedIndex;
            if (idx >= 0 && idx < s_macroManager.Macros.Count)
            {
                s_lastRecordedMacro = s_macroManager.Macros[idx];
                dialog.Close();
                PlayMacro();
            }
        };

        var deleteBtn = MakeDialogButton("Delete");
        deleteBtn.Click += (_, _) =>
        {
            int idx = listBox.SelectedIndex;
            if (idx >= 0 && idx < s_macroManager.Macros.Count)
            {
                s_macroManager.Remove(s_macroManager.Macros[idx]);
                RefreshMacroList(listBox);
            }
        };

        buttonPanel.Controls.Add(closeBtn);
        buttonPanel.Controls.Add(playBtn);
        buttonPanel.Controls.Add(deleteBtn);

        dialog.Controls.Add(listBox);
        dialog.Controls.Add(buttonPanel);
        dialog.ShowDialog(FindForm());
    }

    private static void RefreshMacroList(ListBox listBox)
    {
        listBox.Items.Clear();
        foreach (Macro m in s_macroManager.Macros)
            listBox.Items.Add(m.ToString());
        if (listBox.Items.Count == 0)
            listBox.Items.Add("(No macros recorded)");
    }

    private void OnMacroActionExecuted(object? sender, MacroPlaybackProgressEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnMacroActionExecuted(sender, e));
            return;
        }
        _caretManager.MoveTo(e.CaretOffset);
    }

    private void OnMacroPlaybackFinished(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnMacroPlaybackFinished(sender, e));
            return;
        }
        _macroPlayer.ActionExecuted -= OnMacroActionExecuted;
        _macroPlayer.PlaybackFinished -= OnMacroPlaybackFinished;
        _tokenCache.Clear();
        RetokenizeAllVisible();
        UpdateScrollBars();
        Invalidate(true);
        TextChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the document content with new text, preserving state.</summary>
    public void ReloadContent(string text)
    {
        Document = new PieceTable(text ?? string.Empty);
        _commandHistory.Clear();
        RetokenizeAllVisible();
    }

    /// <summary>Undoes the last edit.</summary>
    public void Undo()
    {
        if (_commandHistory.CanUndo)
        {
            _commandHistory.Undo();
            Invalidate(true);
        }
    }

    /// <summary>Redoes the last undone edit.</summary>
    public void Redo()
    {
        if (_commandHistory.CanRedo)
        {
            _commandHistory.Redo();
            Invalidate(true);
        }
    }

    /// <summary>
    /// Applies a transformation function to the currently selected text and replaces it.
    /// The operation is recorded as a single undoable command.
    /// </summary>
    public void TransformSelection(Func<string, string> transform)
    {
        if (_selectionManager.IsColumnMode) return;
        if (!_selectionManager.HasSelection) return;

        long start = _selectionManager.SelectionStart;
        long end = _selectionManager.SelectionEnd;
        int length = (int)(end - start);
        if (length <= 0) return;

        string original = _document.GetText(start, length);
        string transformed = transform(original);
        if (transformed == original) return;

        var cmd = new Core.Commands.ReplaceCommand(_document, start, length, transformed);
        _commandHistory.Execute(cmd);

        // Re-select the transformed text.
        _selectionManager.StartSelection(start);
        _selectionManager.ExtendSelection(start + transformed.Length);
        _caretManager.MoveTo(start + transformed.Length);

        UpdateScrollBars();
        RetokenizeAllVisible();
        Invalidate(true);
        TextChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies a transformation function to the entire document text and replaces it.
    /// The operation is recorded as a single undoable command.
    /// </summary>
    public void TransformDocument(Func<string, string> transform)
    {
        string original = _document.ToString();
        string transformed = transform(original);
        if (transformed == original) return;

        var cmd = new Core.Commands.ReplaceCommand(_document, 0, _document.Length, transformed);
        _commandHistory.Execute(cmd);

        _selectionManager.ClearSelection();
        _caretManager.MoveTo(0);

        UpdateScrollBars();
        RetokenizeAllVisible();
        Invalidate(true);
        TextChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Replaces a range of text at a specific offset, recorded as an undoable command.
    /// Used by Find/Replace to ensure the document is marked dirty.
    /// </summary>
    public void ReplaceRange(long offset, long length, string newText)
    {
        var cmd = new Core.Commands.ReplaceCommand(_document, offset, length, newText);
        _commandHistory.Execute(cmd);

        UpdateScrollBars();
        RetokenizeAllVisible();
        Invalidate(true);
        TextChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Replaces multiple ranges in a single undoable operation.
    /// Replacements must be provided in reverse document order (end to start)
    /// so that earlier offsets remain valid as later ones are modified.
    /// </summary>
    public void ReplaceAllRanges(IReadOnlyList<(long Offset, long Length, string Replacement)> replacements)
    {
        if (replacements.Count == 0) return;

        var commands = new List<Core.Commands.ICommand>(replacements.Count);
        foreach (var (offset, length, replacement) in replacements)
            commands.Add(new Core.Commands.ReplaceCommand(_document, offset, length, replacement));

        var composite = new Core.Commands.CompositeCommand("Replace All", commands);
        _commandHistory.Execute(composite);

        _selectionManager.ClearSelection();
        UpdateScrollBars();
        RetokenizeAllVisible();
        Invalidate(true);
        TextChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Copies the selected text to the clipboard.</summary>
    public void Copy()
    {
        _inputHandler.ProcessKeyDown(Keys.C, ctrl: true, shift: false, alt: false);
    }

    /// <summary>Cuts the selected text to the clipboard.</summary>
    public void Cut()
    {
        _inputHandler.ProcessKeyDown(Keys.X, ctrl: true, shift: false, alt: false);
    }

    /// <summary>Pastes text from the clipboard.</summary>
    public void Paste()
    {
        _inputHandler.ProcessKeyDown(Keys.V, ctrl: true, shift: false, alt: false);
    }

    /// <summary>Deletes the current selection.</summary>
    public void DeleteSelection()
    {
        if (!_selectionManager.HasSelection) return;
        _inputHandler.ProcessKeyDown(Keys.Delete, ctrl: false, shift: false, alt: false);
    }

    /// <summary>Selects all text in the document.</summary>
    public void SelectAll()
    {
        _selectionManager.SelectAll();
        if (_document.Length > 0)
            _caretManager.MoveTo(_document.Length);
        Invalidate(true);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Code folding
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Toggles the fold region at the caret.</summary>
    public void ToggleFoldAtCaret()
    {
        var region = _foldingManager.GetFoldRegionContaining(_caretManager.Line);
        if (region.HasValue)
            _foldingManager.Toggle(region.Value.StartLine);
    }

    /// <summary>Collapses the fold region at the caret.</summary>
    public void FoldAtCaret()
    {
        var region = _foldingManager.GetFoldRegionContaining(_caretManager.Line);
        if (region.HasValue)
            _foldingManager.Collapse(region.Value.StartLine);
    }

    /// <summary>Expands the fold region at the caret.</summary>
    public void UnfoldAtCaret()
    {
        var region = _foldingManager.GetFoldRegionContaining(_caretManager.Line);
        if (region.HasValue)
            _foldingManager.Expand(region.Value.StartLine);
    }

    /// <summary>Collapses all foldable regions.</summary>
    public void FoldAll() => _foldingManager.CollapseAll();

    /// <summary>Expands all collapsed regions.</summary>
    public void UnfoldAll() => _foldingManager.ExpandAll();

    private void ApplyZoom()
    {
        float newSize = Math.Max(DefaultMinZoomFontSize, DefaultFontSize + _zoomLevel);
        _surface.Font = new Font(_surface.Font.FontFamily, newSize, _surface.Font.Style, GraphicsUnit.Point);
        UpdateScrollBars();
        _gutterPanel.Invalidate();
        Invalidate(true);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies the current static default settings (font, tab width, etc.) to this editor instance.
    /// Call after changing any <c>Default*</c> static properties.
    /// </summary>
    public void ApplySettings()
    {
        _surface.Font = new Font(DefaultFontFamily, Math.Max(DefaultMinZoomFontSize, DefaultFontSize + _zoomLevel), FontStyle.Regular, GraphicsUnit.Point);
        _surface.TabSize = DefaultTabWidth;
        _inputHandler.TabSize = DefaultTabWidth;
        _caretManager.BlinkInterval = DefaultCaretBlinkRate;
        UpdateScrollBars();
        _gutterPanel.Invalidate();
        Invalidate(true);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Theme
    // ────────────────────────────────────────────────────────────────────

    private void ApplyTheme(ITheme theme)
    {
        _surface.Theme = theme;
        _gutterRenderer.ApplyTheme(theme);
        _gutterPanel.BackColor = theme.GutterBackground;
        _vScrollBar.BackColor = theme.ScrollBarBackground;
        _hScrollBar.BackColor = theme.ScrollBarBackground;
        BackColor = theme.EditorBackground;

        if (_hexEditor is not null)
            _hexEditor.Theme = theme;

        // Context menu theming.
        _contextMenu.Renderer = new ThemedContextMenuRenderer(theme);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Scrollbar / layout updates
    // ────────────────────────────────────────────────────────────────────

    private void UpdateScrollBars()
    {
        long totalVisibleLines;
        if (_wordWrap)
        {
            totalVisibleLines = _surface.GetTotalWrapRows();
        }
        else
        {
            totalVisibleLines = _foldingManager.GetVisibleLineCount(_document.LineCount);
        }
        int maxLineWidth = _wordWrap ? 0 : EstimateMaxLineWidth();

        _scrollManager.UpdateScrollBars(
            totalVisibleLines,
            maxLineWidth,
            _surface.VisibleLineCount,
            _surface.MaxVisibleColumns);
    }

    /// <summary>
    /// Estimates the width of the longest visible line (in characters).
    /// For performance, samples the currently visible lines rather than
    /// scanning the entire document.
    /// </summary>
    private int EstimateMaxLineWidth()
    {
        int maxWidth = 80; // Reasonable default minimum.
        long firstVisible = _scrollManager.FirstVisibleLine;
        int visibleCount = _surface.VisibleLineCount + 1;

        // Determine doc line range for visible lines.
        long minDocLine = long.MaxValue, maxDocLine = long.MinValue;
        for (int i = 0; i < visibleCount; i++)
        {
            long docLine = _foldingManager.VisibleLineToDocumentLine(firstVisible + i);
            if (docLine >= _document.LineCount) break;
            if (docLine < minDocLine) minDocLine = docLine;
            if (docLine > maxDocLine) maxDocLine = docLine;
        }

        if (minDocLine > maxDocLine) return maxWidth;

        // Batch-fetch lines instead of per-line GetLine calls.
        int rangeCount = (int)(maxDocLine - minDocLine + 1);
        var lineData = _document.GetLineRange(minDocLine, rangeCount);

        for (int i = 0; i < lineData.Length; i++)
        {
            string text = lineData[i].Text;
            // For extremely long lines, use the raw character count as an
            // approximation to avoid iterating millions of characters.
            int expanded = text.Length > 100_000 ? text.Length : ExpandedLength(text);
            if (expanded > maxWidth) maxWidth = expanded;
        }

        return maxWidth;
    }

    private int ExpandedLength(string text)
    {
        int col = 0;
        int tabSize = _surface.TabSize;
        foreach (char c in text)
        {
            if (c == '\t')
                col += tabSize - (col % tabSize);
            else
                col++;
        }
        return col;
    }

    private int ExpandedLength(string text, int charIndex)
    {
        int col = 0;
        int limit = Math.Min(charIndex, text.Length);
        int tabSize = _surface.TabSize;
        for (int i = 0; i < limit; i++)
        {
            if (text[i] == '\t')
                col += tabSize - (col % tabSize);
            else
                col++;
        }
        return col;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Syntax highlighting
    // ────────────────────────────────────────────────────────────────────

    private void RetokenizeAllVisible()
    {
        if (_lexer is null || _document.Length == 0) return;

        long firstVisible = _scrollManager.FirstVisibleLine;
        int visibleCount = _surface.VisibleLineCount + 2;

        // Map visible-line indices to document lines so we tokenize the
        // correct range when folds are active.
        long minDocLine = long.MaxValue, maxDocLine = long.MinValue;

        if (_wordWrap)
        {
            // firstVisible is a wrap-row index; map to document lines.
            var (startDoc, _) = _surface.WrapRowToDocumentLine(firstVisible);
            int wrapCols = _surface.WrapColumns;
            int rowsBudget = visibleCount;
            for (long dl = startDoc; dl < _document.LineCount && rowsBudget > 0; dl++)
            {
                if (!_foldingManager.IsLineVisible(dl))
                    continue;
                if (dl < minDocLine) minDocLine = dl;
                if (dl > maxDocLine) maxDocLine = dl;
                long len = _document.GetLineLength(dl);
                int rows = len <= wrapCols ? 1
                    : Math.Max(1, (int)((len + wrapCols - 1) / wrapCols));
                rowsBudget -= rows;
            }
        }
        else
        {
            for (int i = 0; i < visibleCount; i++)
            {
                long docLine = _foldingManager.VisibleLineToDocumentLine(firstVisible + i);
                if (docLine >= _document.LineCount) break;
                if (docLine < minDocLine) minDocLine = docLine;
                if (docLine > maxDocLine) maxDocLine = docLine;
            }
        }

        if (minDocLine > maxDocLine || minDocLine >= _document.LineCount) return;

        long lastLine = Math.Min(maxDocLine + 1, _document.LineCount);

        // Check if all visible document lines already have valid tokens cached.
        bool allCached = true;
        for (long dl = minDocLine; dl <= maxDocLine; dl++)
        {
            if (_tokenCache.GetCachedTokens(dl) is null)
            {
                allCached = false;
                break;
            }
        }

        if (allCached) return; // Nothing to do.

        // Find the start state by scanning backward from the first visible
        // document line.  Limit the backward scan to avoid tokenizing the
        // entire document when jumping to a distant position.
        long tokenizeFrom = minDocLine;
        LexerState currentState = LexerState.Normal;

        for (long line = minDocLine - 1; line >= 0; line--)
        {
            LexerState? state = _tokenCache.GetCachedState(line);
            if (state.HasValue)
            {
                currentState = state.Value;
                tokenizeFrom = line + 1;
                break;
            }

            if (minDocLine - line > 500)
            {
                // Too far back — start from line 0 with Normal state rather
                // than scanning thousands of lines for a cached state.
                tokenizeFrom = 0;
                currentState = LexerState.Normal;
                break;
            }
        }

        if (tokenizeFrom < minDocLine)
            tokenizeFrom = 0;  // No cached state found; start from beginning.

        // Batch-fetch all lines we need to tokenize.
        int batchCount = (int)(lastLine - tokenizeFrom);
        var lineData = _document.GetLineRange(tokenizeFrom, batchCount);

        for (int i = 0; i < lineData.Length; i++)
        {
            long line = tokenizeFrom + i;
            var (tokens, endState) = _lexer.Tokenize(lineData[i].Text, currentState);
            _tokenCache.SetCache(line, tokens, endState);
            currentState = endState;
        }
    }

    private void DetectLanguageFromContent()
    {
        // Placeholder for shebang / modeline detection.
    }

    /// <summary>
    /// Ensures the editor surface has focus and forces a full
    /// scrollbar update and synchronous repaint.
    /// Call after making the editor visible for the first time.
    /// </summary>
    public void ActivateAndRefresh()
    {
        _surface.Focus();
        UpdateScrollBars();
        RetokenizeAllVisible();
        _surface.Invalidate();
        _surface.Update();
        _gutterPanel.Invalidate();
        _gutterPanel.Update();
    }

    private void DetectFoldingRegions()
    {
        if (_language.Length > 0)
        {
            // Language fold detection uses per-line GetLine() — too expensive for large docs.
            if (_document.Length >= FoldingMaxFileSize) return;
            _foldingManager.DetectFoldingRegions(_document, _language);
        }
        else if (_customBlockRegions is not null)
        {
            _foldingManager.SetRegions(_customBlockRegions
                .Where(b => b.Foldable)
                .Select(b => new FoldRegion(b.StartLine, b.EndLine)));
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Gutter painting
    // ────────────────────────────────────────────────────────────────────

    private void OnGutterPaint(object? sender, PaintEventArgs e)
    {
        _gutterRenderer.UpdateWidth(_document.LineCount, _surface.Font, e.Graphics);

        if (_gutterPanel.Width != _gutterRenderer.Width)
            _gutterPanel.Width = _gutterRenderer.Width;

        _gutterRenderer.Render(
            e.Graphics,
            _surface.Font,
            _surface.LineHeight,
            _scrollManager.FirstVisibleLine,
            _surface.VisibleLineCount + 1,
            _document.LineCount,
            _caretManager.Line,
            _foldingManager,
            _theme,
            _wordWrap ? (Func<long, int>)_surface.GetWrapRowCount : null,
            _wordWrap ? (Func<long, (long, int)>)_surface.WrapRowToDocumentLine : null);
    }

    private void OnGutterMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var wrapFunc = _wordWrap ? (Func<long, int>)_surface.GetWrapRowCount : null;
        Func<long, (long, int)>? wrapMapFunc = _wordWrap ? _surface.WrapRowToDocumentLine : null;
        long line = _gutterRenderer.GetLineFromY(
            e.Y, _surface.LineHeight, _scrollManager.FirstVisibleLine, _foldingManager, wrapFunc, wrapMapFunc);

        if (_gutterRenderer.IsFoldButtonHit(e.X))
        {
            // Toggle fold.
            if (_foldingManager.IsFoldStart(line))
            {
                _foldingManager.Toggle(line);
            }
        }
        else
        {
            // Select line.
            if (line >= 0 && line < _document.LineCount)
            {
                _selectionManager.SelectLine(line);
                long selEnd = _selectionManager.SelectionEnd;
                _caretManager.MoveTo(selEnd);
                _surface.Invalidate();
            }
        }
    }

    private void OnGutterMouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_selectionManager.HasSelection) return;

        var wrapFunc2 = _wordWrap ? (Func<long, int>)_surface.GetWrapRowCount : null;
        Func<long, (long, int)>? wrapMapFunc2 = _wordWrap ? _surface.WrapRowToDocumentLine : null;
        long line = _gutterRenderer.GetLineFromY(
            e.Y, _surface.LineHeight, _scrollManager.FirstVisibleLine, _foldingManager, wrapFunc2, wrapMapFunc2);

        if (line >= 0 && line < _document.LineCount)
        {
            long lineEnd;
            if (line + 1 < _document.LineCount)
                lineEnd = _document.GetLineStartOffset(line + 1);
            else
                lineEnd = _document.Length;

            _selectionManager.ExtendSelection(lineEnd);
            _caretManager.MoveTo(lineEnd);
            _surface.Invalidate();
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Event handlers
    // ────────────────────────────────────────────────────────────────────

    private void OnScrollChanged()
    {
        // Tokenize newly visible lines BEFORE repainting so tokens are ready.
        RetokenizeAllVisible();

        // Repaint both gutter and surface synchronously together so they
        // scroll in lock-step. Previously the gutter was Updated first,
        // tokenization ran, and the surface was only Invalidated (queued),
        // causing the text to visibly lag behind the line numbers.
        _gutterPanel.Invalidate();
        _surface.Invalidate();
        _gutterPanel.Update();
        _surface.Update();
    }

    private void OnCaretMoved(long newOffset)
    {
        _caretManager.EnsureVisible(_scrollManager, _surface.VisibleLineCount, _surface.MaxVisibleColumns);
        CaretMoved?.Invoke(this, newOffset);
        CaretPositionChanged?.Invoke(this, EventArgs.Empty);
        _surface.Invalidate();
        _gutterPanel.Invalidate();
    }

    private void OnBlinkStateChanged(bool visible)
    {
        _surface.Invalidate();
    }

    private void OnSelectionChanged()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        _surface.Invalidate();
    }

    private void OnFoldingChanged()
    {
        // If the caret is now hidden inside a collapsed fold, move it to
        // the fold's start line so it stays visible and anchored.
        if (!_foldingManager.IsLineVisible(_caretManager.Line))
        {
            long visLine = _foldingManager.NextVisibleLine(
                _caretManager.Line, _document.LineCount, forward: false);
            _caretManager.MoveToLineColumn(visLine, 0);
        }

        UpdateScrollBars();
        RetokenizeAllVisible();
        _surface.Invalidate();
        _gutterPanel.Invalidate();
    }

    private void OnTextModified()
    {
        UpdateScrollBars();

        // Incremental re-lexing from the edit point, then ensure all
        // visible lines are tokenized (handles multi-line paste, etc.).
        if (_lexer is not null)
        {
            _tokenCache.IncrementalRelex(_caretManager.Line, _document, _lexer);
            RetokenizeAllVisible();
        }

        TextChanged?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
        _surface.Invalidate();
        _gutterPanel.Invalidate();
    }

    private void OnDocumentTextChanged(object? sender, Bascanka.Core.Buffer.TextChangedEventArgs e)
    {
        // Adjust token cache for line insertions/deletions.
        if (e.NewLength > 0 || e.OldLength > 0)
        {
            // Derive the affected line from the change offset rather than
            // the caret position — the caret may not have moved yet when a
            // bulk replacement (e.g. JSON Format) fires this event.
            long changeLine = 0;
            if (e.Offset > 0 && e.Offset <= _document.Length)
            {
                (changeLine, _) = _document.OffsetToLineColumn(e.Offset);
            }
            _tokenCache.Invalidate(changeLine, _document.LineCount - changeLine);
        }

        // Update live byte-size estimate.
        RecalcFileSizeBytes();
    }

    private void RecalcFileSizeBytes()
    {
        var encoding = _encodingManager?.CurrentEncoding
            ?? new System.Text.UTF8Encoding(false);

        long charCount = _document.Length;

        // Account for line ending expansion (\n → \r\n adds one byte per break).
        if (_lineEnding == "CRLF" && _document.LineCount > 1)
            charCount += _document.LineCount - 1;

        if (encoding is System.Text.UnicodeEncoding)
            _fileSizeBytes = charCount * 2 + (_encodingManager?.HasBom == true ? 2 : 0);
        else if (encoding is System.Text.UTF32Encoding)
            _fileSizeBytes = charCount * 4 + (_encodingManager?.HasBom == true ? 4 : 0);
        else if (encoding.IsSingleByte)
            _fileSizeBytes = charCount + (_encodingManager?.HasBom == true ? encoding.GetPreamble().Length : 0);
        else
        {
            // UTF-8 or other variable-width: compute from text for small files.
            if (_document.Length <= 5_000_000)
            {
                string text = _document.ToString();
                if (_lineEnding == "CRLF")
                    text = text.Replace("\n", "\r\n");
                else if (_lineEnding == "CR")
                    text = text.Replace("\n", "\r");
                _fileSizeBytes = encoding.GetByteCount(text)
                    + (_encodingManager?.HasBom == true ? encoding.GetPreamble().Length : 0);
            }
            // else: keep the last known value (from disk load/save).
        }
    }

    private void OnSavePointChanged(object? sender, EventArgs e)
    {
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private readonly List<ToolStripItem> _ctxSelectionItems = new();

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        bool hasSel = _selectionManager.HasSelection || _selectionManager.HasColumnSelection;
        _ctxUndo.Enabled = _commandHistory.CanUndo;
        _ctxRedo.Enabled = _commandHistory.CanRedo;
        _ctxCut.Enabled = hasSel;
        _ctxCopy.Enabled = hasSel;
        _ctxDelete.Enabled = hasSel;
        _ctxPaste.Enabled = true;
        _ctxSelectAll.Enabled = _document.Length > 0;

        // Enable/disable items that require selection.
        foreach (var item in _ctxSelectionItems)
            item.Enabled = hasSel;
    }

    /// <summary>
    /// Updates context menu text for localization.
    /// </summary>
    public void SetContextMenuTexts(string undo, string redo, string cut, string copy,
        string paste, string delete, string selectAll)
    {
        _ctxUndo.Text = undo;
        _ctxRedo.Text = redo;
        _ctxCut.Text = cut;
        _ctxCopy.Text = copy;
        _ctxPaste.Text = paste;
        _ctxDelete.Text = delete;
        _ctxSelectAll.Text = selectAll;
    }

    /// <summary>
    /// Adds extra items to the editor context menu. Items tagged with
    /// "RequiresSelection" will be enabled/disabled based on selection state.
    /// </summary>
    public void AddContextMenuItems(ToolStripItem[] items, ToolStripItem[]? selectionItems = null)
    {
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.AddRange(items);
        if (selectionItems is not null)
            _ctxSelectionItems.AddRange(selectionItems);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Cleanup
    // ────────────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ContentChanged -= OnContentChangedForHexSync;
            _hexSyncTimer?.Stop();
            _hexSyncTimer?.Dispose();
            _hexEditor?.Dispose();
            _hexSplit?.Dispose();
            _contextMenu.Dispose();
            _findPanel?.Dispose();
            _caretManager.Dispose();
            _surface.Dispose();
            _gutterPanel.Dispose();
            _vScrollBar.Dispose();
            _hScrollBar.Dispose();
            _document.Dispose();
        }

        base.Dispose(disposing);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Double-buffered panel (used for the gutter)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Custom renderer for the editor context menu that uses theme colours
    /// for background, hover highlight, text, and separators.
    /// </summary>
    private sealed class ThemedContextMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly ITheme _theme;

        public ThemedContextMenuRenderer(ITheme theme) => _theme = theme;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(_theme.MenuBackground);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(Point.Empty, e.Item.Size);
            Color bg = e.Item.Selected || e.Item.Pressed ? _theme.MenuHighlight : _theme.MenuBackground;
            using var brush = new SolidBrush(bg);
            e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = _theme.MenuForeground;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            Color sep = Color.FromArgb(
                _theme.MenuBackground.A,
                Math.Min(255, _theme.MenuBackground.R + 30),
                Math.Min(255, _theme.MenuBackground.G + 30),
                Math.Min(255, _theme.MenuBackground.B + 30));
            using var pen = new Pen(sep);
            e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(_theme.MenuBackground);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Color border = Color.FromArgb(
                _theme.MenuBackground.A,
                Math.Min(255, _theme.MenuBackground.R + 40),
                Math.Min(255, _theme.MenuBackground.G + 40),
                Math.Min(255, _theme.MenuBackground.B + 40));
            using var pen = new Pen(border);
            e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }
    }

    /// <summary>
    /// A <see cref="Panel"/> subclass with double-buffering enabled to
    /// eliminate flicker during rapid repainting (e.g. scrolling).
    /// </summary>
    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
        }
    }
}

/// <summary>
/// Event arguments for the Find All operation, carrying the search options
/// so that MainForm can run the search with progress reporting.
/// </summary>
public sealed class FindAllEventArgs : EventArgs
{
    /// <summary>The search pattern that was used.</summary>
    public string SearchPattern { get; }

    /// <summary>The search options to use.</summary>
    public SearchOptions Options { get; }

    public FindAllEventArgs(string searchPattern, SearchOptions options)
    {
        SearchPattern = searchPattern;
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }
}

/// <summary>
/// Event arguments for the Find All in Tabs operation, carrying the search options.
/// </summary>
public sealed class FindAllInTabsEventArgs : EventArgs
{
    /// <summary>The search options to use for the multi-tab search.</summary>
    public SearchOptions Options { get; }

    public FindAllInTabsEventArgs(SearchOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }
}
