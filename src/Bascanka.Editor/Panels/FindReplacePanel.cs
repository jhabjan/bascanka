using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bascanka.Core.Buffer;
using Bascanka.Core.Search;
using Bascanka.Editor.Controls;
using Bascanka.Editor.Themes;
using static Enums;

namespace Bascanka.Editor.Panels;

/// <summary>
/// A modeless find/replace panel anchored to the top-right of the editor area,
/// styled similarly to VS Code's search widget.
/// </summary>
public class FindReplacePanel : UserControl
{
    // ── Constants ─────────────────────────────────────────────────────
    /// <summary>Configurable max search history entries (default 25).</summary>
    public static int ConfigMaxHistoryItems { get; set; } = 25;

    /// <summary>Returns a copy of the current search history.</summary>
    public static IReadOnlyList<string> GetSearchHistory() => [.. _searchHistory];

    /// <summary>Loads search history from disk.</summary>
    public static void LoadSearchHistoryFromDisk()
    {
        try
        {
            if (!File.Exists(SearchHistoryPath)) return;
            string json = File.ReadAllText(SearchHistoryPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            var items = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    items.Add(item.GetString() ?? string.Empty);
            }
            SetSearchHistory(items);
        }
        catch
        {
            // Ignore malformed history file.
        }
    }

    /// <summary>Replaces the search history with the given items.</summary>
    public static void SetSearchHistory(IEnumerable<string> items)
    {
        _searchHistory.Clear();
        foreach (string item in items)
        {
            if (!string.IsNullOrWhiteSpace(item) && _searchHistory.Count < ConfigMaxHistoryItems)
                _searchHistory.Add(item);
        }
        SaveSearchHistory();
    }
    private static int DebounceMsec => EditorControl.DefaultSearchDebounce;
    private const int PanelWidth = 520;
    private const int FindRowHeight = 40;
    private const int ReplaceRowHeight = 40;
    private const int PanelPadding = 8;

    // ── Controls: Find row ────────────────────────────────────────────
    private readonly ComboBox _searchBox;
    private readonly PanelButton _btnMatchCase;
    private readonly PanelButton _btnWholeWord;
    private readonly PanelButton _btnRegex;
    private readonly PanelButton _btnFindNext;
    private readonly PanelButton _btnFindPrev;
    private readonly PanelButton _btnCount;
    private readonly PanelButton _btnMarkAll;
    private readonly PanelButton _btnFindAll;
    private readonly PanelButton _btnFindAllTabs;
    private readonly Label _statusLabel;
    private readonly PanelButton _btnClose;

    // ── Controls: Replace row ─────────────────────────────────────────
    private readonly TextBox _replaceBox;
    private readonly PanelButton _btnReplace;
    private readonly PanelButton _btnReplaceAll;
    private readonly PanelButton _btnExpandReplace;
    private readonly Panel _replaceRow;

    // ── State ─────────────────────────────────────────────────────────
    private readonly SearchEngine _searchEngine = new();
    private static readonly List<string> _searchHistory = [];
    private static readonly string SearchHistoryPath = Path.Combine(GetAppDataFolder(), "search-history.json");
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private CancellationTokenSource? _searchCts;
    private bool _matchCase;
    private bool _wholeWord;
    private bool _useRegex;
    private bool _replaceVisible;
    private int _currentMatchIndex;
    private List<SearchResult> _currentMatches = [];

    // ── External references ───────────────────────────────────────────
    private EditorControl? _editor;
    private PieceTable? _buffer;
    private ITheme? _theme;

    // ── Theme cache ───────────────────────────────────────────────────
    private Color _panelBg;
    private Color _panelFg;
    private Color _inputBg;
    private Color _buttonBg;
    private Color _buttonFg;
    private Color _buttonHoverBg;
    private Color _buttonBorderColor;
    private Color _toggleActiveBg;
    private Color _toggleActiveBorder;
    private Color _toggleActiveFg;
    private Color _borderColor;

    // ── Events ────────────────────────────────────────────────────────

    /// <summary>Raised when the user clicks the Close button or presses Escape.</summary>
    public event EventHandler? PanelClosed;

    /// <summary>Raised when the user navigates to a match (Find Next/Prev).</summary>
    public event EventHandler<SearchResult>? NavigateToMatch;

    /// <summary>Raised when the search highlight pattern changes (for viewport rendering).</summary>
    public event EventHandler<Regex?>? MatchesHighlighted;

    /// <summary>Raised when Find Next/Previous needs a background search with progress (large files).</summary>
    public event EventHandler<FindNextRequestEventArgs>? FindNextRequested;

    /// <summary>Raised when "Find All" is clicked, requesting MainForm to run the search with progress.</summary>
    public event EventHandler<SearchOptions>? FindAllRequested;

    /// <summary>Raised when "Find All in Tabs" is clicked, requesting a multi-tab search.</summary>
    public event EventHandler<SearchOptions>? FindAllInTabsRequested;

    // ── Construction ──────────────────────────────────────────────────

    public FindReplacePanel()
    {
        // Right-aligned, not full-width docked.
        Dock = DockStyle.Top;
        Height = FindRowHeight + 30 + PanelPadding * 2;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        // ── Debounce timer ────────────────────────────────────────────
        _debounceTimer = new System.Windows.Forms.Timer { Interval = DebounceMsec };
        _debounceTimer.Tick += OnDebounceTick;

        // ── Search box (combo for history) ────────────────────────────
        _searchBox = new ComboBox
        {
            Width = 220,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
        };
        _searchBox.TextChanged += (_, _) => RestartDebounce();
        _searchBox.KeyDown += OnSearchBoxKeyDown;
        // Populate from shared history.
        foreach (string item in _searchHistory)
            _searchBox.Items.Add(item);

        // ── Toggle buttons ────────────────────────────────────────────
        _btnMatchCase = CreateToggleButton("Aa", "Match Case");
        _btnMatchCase.Click += (_, _) => { _matchCase = !_matchCase; _btnMatchCase.IsActive = _matchCase; RunIncrementalSearch(); };

        _btnWholeWord = CreateToggleButton("W", "Whole Word");
        _btnWholeWord.Click += (_, _) => { _wholeWord = !_wholeWord; _btnWholeWord.IsActive = _wholeWord; RunIncrementalSearch(); };

        _btnRegex = CreateToggleButton(".*", "Use Regular Expression");
        _btnRegex.Click += (_, _) => { _useRegex = !_useRegex; _btnRegex.IsActive = _useRegex; RunIncrementalSearch(); };

        // ── Action buttons ────────────────────────────────────────────
        _btnFindPrev = CreateIconButton("\u25C0", "Find Previous");
        _btnFindPrev.Click += (_, _) => FindPrevious();

        _btnFindNext = CreateIconButton("\u25B6", "Find Next");
        _btnFindNext.Click += (_, _) => FindNext();

        _btnCount = CreateIconButton("#", "Count Matches");
        _btnCount.Click += (_, _) => CountMatches();

        _btnMarkAll = CreateTextButton("Mark All", "Highlight All Matches");
        _btnMarkAll.Click += (_, _) => MarkAll();

        _btnFindAll = CreateTextButton("Find All", "Find All in Current Document");
        _btnFindAll.Click += (_, _) => FindAll();

        _btnFindAllTabs = CreateTextButton("Find in Tabs", "Find All in All Open Tabs");
        _btnFindAllTabs.Click += (_, _) => FindAllInTabs();

        // ── Status ────────────────────────────────────────────────────
        _statusLabel = new Label
        {
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(4, 5, 2, 0),
        };

        // ── Close button ──────────────────────────────────────────────
        _btnClose = CreateIconButton("\u2715", "Close (Esc)");
        _btnClose.Click += (_, _) => ClosePanel();

        // ── Expand/collapse replace ───────────────────────────────────
        _btnExpandReplace = CreateIconButton("\u25BC", "Toggle Replace");
        _btnExpandReplace.Click += (_, _) => ToggleReplace();

        // ── Replace row ───────────────────────────────────────────────
        _replaceBox = new TextBox
        {
            Width = 220,
            Height = 26,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f),
        };
        _replaceBox.KeyDown += OnReplaceBoxKeyDown;

        _btnReplace = CreateTextButton("Replace", "Replace Current Match");
        _btnReplace.Click += (_, _) => ReplaceCurrent();

        _btnReplaceAll = CreateTextButton("All", "Replace All Matches");
        _btnReplaceAll.Click += (_, _) => ReplaceAll();

        _replaceRow = new Panel
        {
            Dock = DockStyle.None,
            Height = ReplaceRowHeight,
            Visible = false,
        };

        LayoutControls();
    }

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Sets the editor control and text buffer that this panel searches within.
    /// </summary>
    public void Attach(EditorControl editor, PieceTable buffer)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

        // Cancel any in-flight search that references the old buffer.
        _searchCts?.Cancel();
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
    /// Updates button text for localization.
    /// </summary>
    public void SetButtonTexts(string markAll, string findAll, string findInTabs,
        string replace, string replaceAll)
    {
        _btnMarkAll.Text = markAll;
        _btnFindAll.Text = findAll;
        _btnFindAllTabs.Text = findInTabs;
        _btnReplace.Text = replace;
        _btnReplaceAll.Text = replaceAll;
    }

    /// <summary>
    /// Sets the search text and optionally focuses the search box.
    /// </summary>
    public void SetSearchText(string text, bool focus = true)
    {
        _searchBox.Text = text;
        if (focus)
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }
    }

    /// <summary>
    /// Whether the replace row is expanded.
    /// </summary>
    public bool ReplaceVisible
    {
        get => _replaceVisible;
        set
        {
            _replaceVisible = value;
            _replaceRow.Visible = value;
            Height = value
                ? FindRowHeight + 30 + ReplaceRowHeight + PanelPadding * 2
                : FindRowHeight + 30 + PanelPadding * 2;
            _btnExpandReplace.Text = value ? "\u25B2" : "\u25BC";
            PositionPanel();
        }
    }

    // ── Search operations ─────────────────────────────────────────────

    public void FindNext()
    {
        if (_buffer is null || string.IsNullOrEmpty(_searchBox.Text)) return;

        AddToHistory(_searchBox.Text);
        SearchOptions options = BuildSearchOptions(searchUp: false);
        long startOffset = GetSearchStartOffset(forward: true);

        _statusLabel.Text = "Searching...";
        FindNextRequested?.Invoke(this, new FindNextRequestEventArgs(options, startOffset));
    }

    public void FindPrevious()
    {
        if (_buffer is null || string.IsNullOrEmpty(_searchBox.Text)) return;

        AddToHistory(_searchBox.Text);
        SearchOptions options = BuildSearchOptions(searchUp: true);
        long startOffset = GetSearchStartOffset(forward: false);

        _statusLabel.Text = "Searching...";
        FindNextRequested?.Invoke(this, new FindNextRequestEventArgs(options, startOffset));
    }

    /// <summary>
    /// Called by MainForm after a background Find Next/Previous completes.
    /// </summary>
    public void DeliverFindNextResult(SearchResult? result)
    {
        if (result is not null)
        {
            _currentMatchIndex = FindMatchIndex(result.Offset);
            UpdateStatusLabel();
            NavigateToMatch?.Invoke(this, result);
        }
        else
        {
            _statusLabel.Text = "No matches";
            _currentMatchIndex = -1;
        }
    }

    public void CountMatches()
    {
        if (_buffer is null || string.IsNullOrEmpty(_searchBox.Text))
        {
            _statusLabel.Text = string.Empty;
            return;
        }

        SearchOptions options = BuildSearchOptions(searchUp: false);
        int count = _searchEngine.CountMatches(_buffer, options);
        _statusLabel.Text = $"{count} match{(count == 1 ? "" : "es")}";
    }

    public async void MarkAll()
    {
        if (_buffer is null || string.IsNullOrEmpty(_searchBox.Text)) return;

        AddToHistory(_searchBox.Text);
        SearchOptions options = BuildSearchOptions(searchUp: false);

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _btnMarkAll.Enabled = false;
        _statusLabel.Text = "Searching...";

        try
        {
            _currentMatches = await _searchEngine.FindAllAsync(_buffer, options,
                cancellationToken: token);
            UpdateStatusLabel();
            MatchesHighlighted?.Invoke(this, _currentMatches.Count > 0 ? BuildHighlightRegex() : null);
        }
        catch (OperationCanceledException)
        {
            // Cancelled — ignore.
        }
        catch (ObjectDisposedException)
        {
            // Buffer was disposed during incremental loading — treat as cancel.
        }
        finally
        {
            _btnMarkAll.Enabled = true;
        }
    }

    public void FindAll()
    {
        if (_buffer is null || string.IsNullOrEmpty(_searchBox.Text)) return;

        AddToHistory(_searchBox.Text);
        SearchOptions options = BuildSearchOptions(searchUp: false);

        // Cancel any in-flight incremental/mark search so it doesn't
        // interfere while MainForm runs the real search with progress.
        _searchCts?.Cancel();

        _statusLabel.Text = "Searching...";

        // Delegate the actual async search to MainForm (which shows a progress overlay).
        FindAllRequested?.Invoke(this, options);
    }

    /// <summary>
    /// Called by MainForm after the search completes to deliver results back
    /// for match highlighting and status label update.
    /// </summary>
    public void SetFindAllResults(List<SearchResult> results)
    {
        _currentMatches = results;
        _currentMatchIndex = _currentMatches.Count > 0 ? 0 : -1;
        UpdateStatusLabel();
        MatchesHighlighted?.Invoke(this, _currentMatches.Count > 0 ? BuildHighlightRegex() : null);
    }

    public void FindAllInTabs()
    {
        if (string.IsNullOrEmpty(_searchBox.Text)) return;

        AddToHistory(_searchBox.Text);
        SearchOptions options = BuildSearchOptions(searchUp: false);
        FindAllInTabsRequested?.Invoke(this, options);
    }

    /// <summary>The current search pattern text.</summary>
    public string SearchPattern => _searchBox.Text;

    public void ReplaceCurrent()
    {
        if (_editor is null || _buffer is null || _currentMatches.Count == 0 || _currentMatchIndex < 0)
            return;

        if (_currentMatchIndex >= _currentMatches.Count) return;

        SearchResult match = _currentMatches[_currentMatchIndex];
        SearchOptions options = BuildSearchOptions(searchUp: false);

        // Expand regex backreferences if needed, then replace through the command system.
        string matchedText = _buffer.GetText(match.Offset, match.Length);
        string expanded = options.UseRegex
            ? SearchEngine.BuildPattern(options).Replace(matchedText, _replaceBox.Text)
            : _replaceBox.Text;

        _editor.ReplaceRange(match.Offset, match.Length, expanded);

        RunIncrementalSearch();
        FindNext();
    }

    public void ReplaceAll()
    {
        if (_editor is null || _buffer is null || string.IsNullOrEmpty(_searchBox.Text)) return;

        SearchOptions options = BuildSearchOptions(searchUp: false);
        List<SearchResult> matches = _searchEngine.FindAll(_buffer, options);
        if (matches.Count == 0)
        {
            _statusLabel.Text = "0 occurrences";
            return;
        }

        Regex? regex = options.UseRegex ? SearchEngine.BuildPattern(options) : null;

        // Build replacements in reverse document order so offsets stay valid.
        var replacements = new List<(long Offset, long Length, string Replacement)>(matches.Count);
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            SearchResult m = matches[i];
            string actual = _buffer.GetText(m.Offset, m.Length);
            string expanded = regex is not null
                ? regex.Replace(actual, _replaceBox.Text)
                : _replaceBox.Text;
            replacements.Add((m.Offset, m.Length, expanded));
        }

        _editor.ReplaceAllRanges(replacements);

        _currentMatches.Clear();
        _currentMatchIndex = -1;
        _statusLabel.Text = $"Replaced {matches.Count} occurrence{(matches.Count == 1 ? "" : "s")}";
    }

    public void ClosePanel()
    {
        _searchCts?.Cancel();
        Visible = false;
        _currentMatches.Clear();
        _currentMatchIndex = -1;
        PanelClosed?.Invoke(this, EventArgs.Empty);
    }

    // ── Keyboard handling ─────────────────────────────────────────────

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle standard edit shortcuts explicitly so they aren't
        // routed to the editor control by the form's KeyPreview.
        if (e.Control && !e.Shift && !e.Alt && sender is ComboBox cb)
        {
            switch (e.KeyCode)
            {
                case Keys.V:
                    if (Clipboard.ContainsText())
                    {
                        string clip = Clipboard.GetText();
                        int sel = cb.SelectionStart;
                        int len = cb.SelectionLength;
                        string txt = cb.Text;
                        cb.Text = txt[..sel] + clip + txt[(sel + len)..];
                        cb.SelectionStart = sel + clip.Length;
                    }
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.C:
                    if (cb.SelectionLength > 0)
                        Clipboard.SetText(cb.Text.Substring(cb.SelectionStart, cb.SelectionLength));
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.X:
                    if (cb.SelectionLength > 0)
                    {
                        Clipboard.SetText(cb.Text.Substring(cb.SelectionStart, cb.SelectionLength));
                        int sel2 = cb.SelectionStart;
                        cb.Text = cb.Text[..sel2] + cb.Text[(sel2 + cb.SelectionLength)..];
                        cb.SelectionStart = sel2;
                    }
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.A:
                    cb.SelectAll();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
            }
        }

        switch (e.KeyCode)
        {
            case Keys.Enter when e.Shift:
                e.SuppressKeyPress = true;
                FindPrevious();
                break;

            case Keys.Enter:
                e.SuppressKeyPress = true;
                FindNext();
                break;

            case Keys.Escape:
                e.SuppressKeyPress = true;
                ClosePanel();
                break;
        }
    }

    private void OnReplaceBoxKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle standard edit shortcuts explicitly so they aren't
        // routed to the editor control by the form's KeyPreview.
        if (e.Control && !e.Shift && !e.Alt && sender is TextBox tb)
        {
            switch (e.KeyCode)
            {
                case Keys.V:
                    tb.Paste();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.C:
                    tb.Copy();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.X:
                    tb.Cut();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.A:
                    tb.SelectAll();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
            }
        }

        switch (e.KeyCode)
        {
            case Keys.Enter:
                e.SuppressKeyPress = true;
                ReplaceCurrent();
                break;

            case Keys.Escape:
                e.SuppressKeyPress = true;
                ClosePanel();
                break;
        }
    }

    // ── Incremental search ────────────────────────────────────────────

    private void RestartDebounce()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        RunIncrementalSearch();
    }

    private async void RunIncrementalSearch()
    {
        if (_buffer is null || string.IsNullOrEmpty(_searchBox.Text))
        {
            _currentMatches.Clear();
            _currentMatchIndex = -1;
            _statusLabel.Text = string.Empty;
            MatchesHighlighted?.Invoke(this, (Regex?)null);
            return;
        }

        SearchOptions options = BuildSearchOptions(searchUp: false);

        // Cancel any previous async search.
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            _currentMatches = await _searchEngine.FindAllAsync(_buffer, options,
                cancellationToken: token);
        }
        catch (OperationCanceledException)
        {
            return; // Cancelled by a newer search.
        }
        catch (ObjectDisposedException)
        {
            return; // Buffer was disposed during incremental loading.
        }
        catch (ArgumentException)
        {
            _currentMatches.Clear();
            _statusLabel.Text = "Invalid pattern";
            return;
        }

        // If cancelled while running, discard partial results.
        if (token.IsCancellationRequested)
            return;

        _currentMatchIndex = _currentMatches.Count > 0 ? 0 : -1;
        UpdateStatusLabel();
        MatchesHighlighted?.Invoke(this, _currentMatches.Count > 0 ? BuildHighlightRegex() : null);

        if (_currentMatches.Count > 0)
            NavigateToMatch?.Invoke(this, _currentMatches[0]);
    }

    // ── History ───────────────────────────────────────────────────────

    private void AddToHistory(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _searchHistory.Remove(text);
        _searchHistory.Insert(0, text);

        if (_searchHistory.Count > ConfigMaxHistoryItems)
            _searchHistory.RemoveAt(_searchHistory.Count - 1);

        _searchBox.BeginUpdate();
        _searchBox.Items.Clear();
        foreach (string item in _searchHistory)
            _searchBox.Items.Add(item);
        _searchBox.EndUpdate();

        SaveSearchHistory();
    }

    private static void SaveSearchHistory()
    {
        try
        {
            string dir = Path.GetDirectoryName(SearchHistoryPath) ?? "";
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(_searchHistory);
            string tmpPath = SearchHistoryPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, SearchHistoryPath, overwrite: true);
        }
        catch
        {
            // Best effort.
        }
    }

    private static string GetAppDataFolder()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
#if DEBUG
        return Path.Combine(baseDir, "Bascanka", "debug");
#else
        return Path.Combine(baseDir, "Bascanka");
#endif
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a compiled <see cref="Regex"/> for highlighting matches on visible lines.
    /// Returns <see langword="null"/> if the pattern is empty or invalid.
    /// </summary>
    private Regex? BuildHighlightRegex()
    {
        if (string.IsNullOrEmpty(_searchBox.Text)) return null;
        try
        {
            return SearchEngine.BuildPattern(BuildSearchOptions(searchUp: false));
        }
        catch (ArgumentException)
        {
            return null; // Invalid regex pattern while typing.
        }
    }

    private SearchOptions BuildSearchOptions(bool searchUp)
    {
        return new SearchOptions
        {
            Pattern = _searchBox.Text,
            MatchCase = _matchCase,
            WholeWord = _wholeWord,
            UseRegex = _useRegex,
            SearchUp = searchUp,
            WrapAround = true,
            Scope = SearchScope.CurrentDocument,
        };
    }

    private long GetSearchStartOffset(bool forward)
    {
        if (_currentMatches.Count == 0 || _currentMatchIndex < 0)
            return 0;

        if (_currentMatchIndex >= _currentMatches.Count)
            return 0;

        SearchResult current = _currentMatches[_currentMatchIndex];
        return forward ? current.Offset + current.Length : current.Offset;
    }

    private int FindMatchIndex(long offset)
    {
        for (int i = 0; i < _currentMatches.Count; i++)
        {
            if (_currentMatches[i].Offset == offset)
                return i;
        }
        return -1;
    }

    private void UpdateStatusLabel()
    {
        if (_currentMatches.Count == 0)
        {
            _statusLabel.Text = "No matches";
        }
        else if (_currentMatchIndex >= 0)
        {
            bool capped = _currentMatches.Count >= Core.Search.SearchEngine.MaxResults;
            string countText = capped
                ? $"{Core.Search.SearchEngine.MaxResults:N0}+"
                : _currentMatches.Count.ToString();
            _statusLabel.Text = $"{_currentMatchIndex + 1} of {countText}";
        }
        else
        {
            bool capped = _currentMatches.Count >= Core.Search.SearchEngine.MaxResults;
            string countText = capped
                ? $"{Core.Search.SearchEngine.MaxResults:N0}+"
                : _currentMatches.Count.ToString();
            _statusLabel.Text = $"{countText} match{(_currentMatches.Count == 1 ? "" : "es")}";
        }
    }

    private void ToggleReplace()
    {
        ReplaceVisible = !ReplaceVisible;
    }

    // ── Layout ────────────────────────────────────────────────────────

    private void PositionPanel()
    {
        // Called on parent resize too — keeps us right-aligned.
        if (Parent is not null)
        {
            int scrollBarW = SystemInformation.VerticalScrollBarWidth;
            int x = Parent.ClientSize.Width - PanelWidth - scrollBarW - 2;
            if (x < 0) x = 0;
            Location = new Point(x, 0);
            Width = Math.Min(PanelWidth, Parent.ClientSize.Width);
        }
    }

    private void LayoutControls()
    {
        // Override dock so we can position ourselves at the right.
        Dock = DockStyle.None;
        Width = PanelWidth;
        Anchor = AnchorStyles.Top | AnchorStyles.Right;

        // ── Find row ─────────────────────────────────────────────────
        var findRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = FindRowHeight,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(PanelPadding, 4, PanelPadding, 0),
        };

        findRow.Controls.AddRange([
            _btnExpandReplace,
            _searchBox,
            _btnMatchCase,
            _btnWholeWord,
            _btnRegex,
            _btnFindPrev,
            _btnFindNext,
            _btnClose,
        ]);

        // ── Second row: action buttons + status ──────────────────────
        var actionsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(PanelPadding + 26, 0, PanelPadding, 0),
        };

        actionsRow.Controls.AddRange([
            _btnCount,
            _btnMarkAll,
            _btnFindAll,
            _btnFindAllTabs,
            _statusLabel,
        ]);

        // ── Replace row ──────────────────────────────────────────────
        var replaceFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(PanelPadding + 26, 4, PanelPadding, 0),
        };

        replaceFlow.Controls.AddRange([_replaceBox, _btnReplace, _btnReplaceAll]);
        _replaceRow.Controls.Add(replaceFlow);
        _replaceRow.Dock = DockStyle.Top;

        Controls.Add(_replaceRow);
        Controls.Add(actionsRow);
        Controls.Add(findRow);

        // Position ourselves when parent resizes.
        ParentChanged += (_, _) =>
        {
            PositionPanel();
            if (Parent is not null)
            {
                Parent.Resize -= OnParentResize;
                Parent.Resize += OnParentResize;
            }
        };
    }

    private void OnParentResize(object? sender, EventArgs e) => PositionPanel();

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) PositionPanel();
    }

    // ── Painting ──────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Draw shadow.
        Color shadowColor = Color.FromArgb(30, 0, 0, 0);
        using var shadowPen = new Pen(shadowColor, 1);
        g.DrawRectangle(shadowPen, 0, 0, Width - 1, Height - 1);

        // Draw border.
        using var borderPen = new Pen(_borderColor, 1);
        g.DrawRectangle(borderPen, 1, 0, Width - 3, Height - 2);
    }

    // ── Theme ─────────────────────────────────────────────────────────

    private void ApplyTheme()
    {
        if (_theme is null) return;

        // Compute palette from theme.
        _panelBg = _theme.FindPanelBackground;
        _panelFg = _theme.FindPanelForeground;
        _inputBg = Lighten(_panelBg, 15);
        _buttonBg = _panelBg;
        _buttonFg = _panelFg;
        _buttonHoverBg = Lighten(_panelBg, 20);
        _buttonBorderColor = Color.FromArgb(50, _panelFg);
        _borderColor = Lighten(_panelBg, 40);

        // Toggle active colours — use the status bar accent for active toggles.
        _toggleActiveBg = Color.FromArgb(60, _theme.StatusBarBackground.R,
            _theme.StatusBarBackground.G, _theme.StatusBarBackground.B);
        _toggleActiveBorder = _theme.StatusBarBackground;
        _toggleActiveFg = _panelFg;

        BackColor = _panelBg;
        ForeColor = _panelFg;

        // Input boxes.
        _searchBox.BackColor = _inputBg;
        _searchBox.ForeColor = _panelFg;
        _replaceBox.BackColor = _inputBg;
        _replaceBox.ForeColor = _panelFg;
        _statusLabel.ForeColor = Color.FromArgb(160, _panelFg);
        _statusLabel.BackColor = _panelBg;

        // Apply to all buttons.
        ApplyButtonTheme(_btnMatchCase);
        ApplyButtonTheme(_btnWholeWord);
        ApplyButtonTheme(_btnRegex);
        ApplyButtonTheme(_btnFindPrev);
        ApplyButtonTheme(_btnFindNext);
        ApplyButtonTheme(_btnCount);
        ApplyButtonTheme(_btnMarkAll);
        ApplyButtonTheme(_btnFindAll);
        ApplyButtonTheme(_btnFindAllTabs);
        ApplyButtonTheme(_btnClose);
        ApplyButtonTheme(_btnExpandReplace);
        ApplyButtonTheme(_btnReplace);
        ApplyButtonTheme(_btnReplaceAll);

        // Flow panels.
        foreach (Control c in Controls)
        {
            if (c is FlowLayoutPanel flow)
                flow.BackColor = _panelBg;
            if (c is Panel panel)
                panel.BackColor = _panelBg;
        }

        // Recurse into replace row children.
        foreach (Control c in _replaceRow.Controls)
        {
            if (c is FlowLayoutPanel flow)
                flow.BackColor = _panelBg;
        }

        Invalidate(true);
    }

    private void ApplyButtonTheme(PanelButton btn)
    {
        btn.NormalBg = _buttonBg;
        btn.HoverBg = _buttonHoverBg;
        btn.ForeColor = _buttonFg;
        btn.BackColor = _panelBg;
        btn.BorderColor = _buttonBorderColor;
        btn.ActiveBg = _toggleActiveBg;
        btn.ActiveBorder = _toggleActiveBorder;
        btn.ActiveFg = _toggleActiveFg;
        btn.Invalidate();
    }

    private static Color Lighten(Color c, int amount)
    {
        return Color.FromArgb(c.A,
            Math.Min(255, c.R + amount),
            Math.Min(255, c.G + amount),
            Math.Min(255, c.B + amount));
    }

    // ── Control factory helpers ───────────────────────────────────────

    private static PanelButton CreateToggleButton(string text, string tooltip)
    {
        var btn = new PanelButton
        {
            Text = text,
            Width = 32,
            Height = 28,
            Margin = new Padding(1, 1, 1, 1),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            ButtonMode = PanelButtonMode.Toggle,
        };

        var tt = new ToolTip { InitialDelay = 400 };
        tt.SetToolTip(btn, tooltip);

        return btn;
    }

    private static PanelButton CreateIconButton(string text, string tooltip)
    {
        var btn = new PanelButton
        {
            Text = text,
            Width = 32,
            Height = 28,
            Margin = new Padding(1, 1, 1, 1),
            Font = new Font("Segoe UI", 9.5f),
            Cursor = Cursors.Hand,
            ButtonMode = PanelButtonMode.Icon,
        };

        var tt = new ToolTip { InitialDelay = 400 };
        tt.SetToolTip(btn, tooltip);

        return btn;
    }

    private static PanelButton CreateTextButton(string text, string tooltip)
    {
        var btn = new PanelButton
        {
            Text = text,
            AutoSize = true,
            Height = 26,
            Margin = new Padding(2, 1, 2, 1),
            Padding = new Padding(8, 2, 8, 2),
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand,
            ButtonMode = PanelButtonMode.Text,
        };

        var tt = new ToolTip { InitialDelay = 400 };
        tt.SetToolTip(btn, tooltip);

        return btn;
    }

    // ── Disposal ──────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _debounceTimer.Dispose();
            Parent?.Resize -= OnParentResize;
        }
        base.Dispose(disposing);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Owner-drawn button control
    // ══════════════════════════════════════════════════════════════════

    
}
