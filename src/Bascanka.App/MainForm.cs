using System.Runtime.InteropServices;
using System.Text;
using Bascanka.Core.Buffer;
using Bascanka.Core.Diff;
using Bascanka.Core.Encoding;
using Bascanka.Core.IO;
using Bascanka.Core.Search;
using Bascanka.Core.Syntax;
using Bascanka.Editor.Controls;
using Bascanka.Editor.Diff;
using Bascanka.Editor.Panels;
using Bascanka.Editor.Tabs;
using Bascanka.Editor.Themes;

namespace Bascanka.App;

/// <summary>
/// Main application window for the Bascanka text editor.
/// Manages multiple document tabs, menus, status bar, and all sub-managers.
/// </summary>
public sealed class MainForm : Form
{
    // ── Layout controls ──────────────────────────────────────────────
    private readonly MenuStrip _menuStrip;
    private readonly TabStrip _tabStrip;
    private readonly StatusStrip _statusStrip;
    private readonly Panel _editorPanel;
    private readonly SplitContainer _verticalSplit;   // left: side panel, right: editor area
    private readonly SplitContainer _horizontalSplit;  // top: editor, bottom: bottom panel
    private readonly Panel _sidePanel;
    private readonly Panel _bottomPanel;

    // ── Find results ─────────────────────────────────────────────────
    private readonly FindResultsPanel _findResultsPanel;

    // ── Bottom panel tabs ──────────────────────────────────────────────
    private readonly BottomPanelTabStrip _bottomTabStrip;
    private readonly Panel _bottomContentPanel;
    private TerminalPanel? _terminalPanel;

    // ── Symbol list ───────────────────────────────────────────────────
    private readonly SymbolListPanel _symbolListPanel;

    // ── Sub-managers ─────────────────────────────────────────────────
    private readonly MenuBuilder _menuBuilder;
    private readonly StatusBarManager _statusBarManager;
    private readonly RecentFilesManager _recentFilesManager;
    private readonly FileWatcher _fileWatcher;
    private readonly PluginHost _pluginHost;
    private readonly KeyboardShortcutManager _shortcutManager;
    private readonly CustomHighlightStore _customHighlightStore;
    private readonly RecoveryManager _recoveryManager;

    // ── Deferred large-file recovery data ─────────────────────────────
    // When a modified large file is restored from crash recovery but its tab
    // is not the active tab, we defer the expensive async loading until the
    // tab is first activated.  The recovery metadata is stashed here and
    // consumed by LoadDeferredTab.
    private sealed record DeferredRecoveryData(
        string AddBuffer, IReadOnlyList<Piece> Pieces,
        int CodePage, bool HasBom, string? LineEnding, string? Language,
        long SavedCaret, int SavedScroll, int SavedZoom,
        Guid? RecoveryTabId, bool WordWrap, string? CustomProfileName);
    private readonly Dictionary<string, DeferredRecoveryData> _deferredRecovery = new(StringComparer.OrdinalIgnoreCase);

    // ── Document state ───────────────────────────────────────────────
    private readonly List<TabInfo> _tabs = [];
    private int _activeTabIndex = -1;
    private int _deferredInsertIndex = -1;
    private int _untitledCounter;
    private bool _isFullScreen;
    private FormWindowState _previousWindowState;
    private FormBorderStyle _previousBorderStyle;

    // ── Search progress overlay ────────────────────────────────────
    private readonly SearchProgressOverlay _searchOverlay;
    private CancellationTokenSource? _searchCts;

    // ── Files passed on command line ─────────────────────────────────
    private readonly string[] _initialFiles;

    // ── Single-instance IPC ───────────────────────────────────────────
    private readonly SingleInstanceManager? _singleInstance;

    /// <summary>
    /// Creates the main form, optionally opening the given files on startup.
    /// </summary>
    public MainForm(string[]? filesToOpen = null, SingleInstanceManager? singleInstance = null)
    {
        _initialFiles = filesToOpen ?? [];
        _singleInstance = singleInstance;

        // Restore saved theme before any controls are created.
        string savedTheme = SettingsManager.GetString(SettingsManager.KeyTheme);
        if (!string.IsNullOrEmpty(savedTheme))
            ThemeManager.Instance.SetTheme(savedTheme);

        // Load all persisted settings into static properties.
        LoadAllSettings();
        ApplyThemeOverridesFromRegistry();
        FindReplacePanel.LoadSearchHistoryFromDisk();

        // ── Form properties ──────────────────────────────────────────
        Text = Strings.AppTitle;
        Size = new Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;

        // Set window icon from embedded resource.
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        using var iconStream = asm.GetManifestResourceStream("Bascanka.App.Resources.bascanka.ico");
        if (iconStream is not null)
            Icon = new Icon(iconStream);
        AllowDrop = true;
        KeyPreview = true;
        DoubleBuffered = true;

        // ── Create layout controls ───────────────────────────────────
        _menuStrip = new MenuStrip { Dock = DockStyle.Top };
        _tabStrip = new TabStrip
        {
            Dock = DockStyle.Top,
            ContextMenuRenderer = t => new ThemedMenuRenderer(t)
        };
        _statusStrip = new StatusStrip { Dock = DockStyle.Bottom };

        _sidePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Width = 250,
        };

        _bottomPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 200,
        };

        _editorPanel = new Panel { Dock = DockStyle.Fill };

        // Horizontal split: editor (top) and bottom panel (bottom).
        _horizontalSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 500,
            Panel2Collapsed = true,
        };
        _horizontalSplit.Panel1.Controls.Add(_editorPanel);
        _horizontalSplit.Panel2.Controls.Add(_bottomPanel);

        // ── Bottom panel tab strip + content area ─────────────────────
        _bottomTabStrip = new BottomPanelTabStrip { Dock = DockStyle.Top };
        _bottomContentPanel = new Panel { Dock = DockStyle.Fill };

        _findResultsPanel = new FindResultsPanel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            ContextMenuRenderer = t => new ThemedMenuRenderer(t),
            ShowHeader = false
        };
        _findResultsPanel.NavigateToResult += OnNavigateToFindResult;
        _findResultsPanel.OpenResultsInNewTab += OnOpenFindResultsInNewTab;
        _findResultsPanel.PageChanging += (_, _) =>
            _searchOverlay.ShowOverlay(this, "Loading results...");
        _findResultsPanel.PageChanged += (_, _) => _searchOverlay.HideOverlay();

        _bottomContentPanel.Controls.Add(_findResultsPanel);

        _bottomTabStrip.AddTab(new BottomPanelTab
        {
            Id = "findResults",
            Title = Strings.FindResultsHeader,
            Content = _findResultsPanel,
            Closable = true,
        });

        _bottomTabStrip.TabSelected += OnBottomTabSelected;
        _bottomTabStrip.TabCloseRequested += OnBottomTabCloseRequested;
        _bottomTabStrip.PanelCloseRequested += (_, _) =>
        {
            IsBottomPanelVisible = false;
            _menuBuilder.UpdateMenuState(this);
        };

        // Layout order: tab strip on top, content fills rest.
        _bottomPanel.Controls.Add(_bottomContentPanel);
        _bottomPanel.Controls.Add(_bottomTabStrip);

        // Select the first tab.
        _bottomTabStrip.SelectTab("findResults");

        // ── Symbol list panel in side panel ─────────────────────────
        _symbolListPanel = new SymbolListPanel { Dock = DockStyle.Fill };
        _symbolListPanel.NavigateToSymbol += OnNavigateToSymbol;
        _sidePanel.Controls.Add(_symbolListPanel);

        // Vertical split: side panel (left) and editor area (right).
        _verticalSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 250,
            Panel1Collapsed = true,
        };
        _verticalSplit.Panel1.Controls.Add(_sidePanel);
        _verticalSplit.Panel2.Controls.Add(_horizontalSplit);

        // ── Tab strip events ─────────────────────────────────────────
        _tabStrip.TabSelected += OnTabSelected;
        _tabStrip.TabClosed += OnTabCloseRequested;
        _tabStrip.TabsReordered += OnTabsReordered;
        _tabStrip.TabContextMenuOpening += OnTabContextMenuOpening;
        _tabStrip.NewTabRequested += (_, _) => NewDocument();

        // ── Add controls in correct Z-order ──────────────────────────
        Controls.Add(_verticalSplit);
        Controls.Add(_tabStrip);
        Controls.Add(_menuStrip);
        Controls.Add(_statusStrip);
        MainMenuStrip = _menuStrip;

        // ── Search progress overlay (hidden by default) ─────────────
        _searchOverlay = new SearchProgressOverlay { Visible = false };
        Controls.Add(_searchOverlay);
        _searchOverlay.BringToFront();

        // ── Initialize sub-managers ──────────────────────────────────
        _shortcutManager = new KeyboardShortcutManager();
        RegisterDefaultShortcuts();

        _statusBarManager = new StatusBarManager(_statusStrip);
        _recentFilesManager = new RecentFilesManager();
        _fileWatcher = new FileWatcher(this);
        _pluginHost = new PluginHost(this);
        _customHighlightStore = new CustomHighlightStore();
        _customHighlightStore.Load();
        _recoveryManager = new RecoveryManager(this);
        _menuBuilder = new MenuBuilder();

        // Build the main menu (wires all menu items).
        _menuBuilder.BuildMenu(this, _menuStrip);
        _menuBuilder.SetPluginsMenuVisible(Program.EnablePlugins);
        _menuBuilder.UpdateMenuState(this);

        // Apply localized texts to controls in the Editor layer.
        ApplyLocalizedMenuTexts();

        // Restore window geometry from previous session (before OnLoad).
        _recoveryManager.RestoreWindowState();

        // Rebuild the entire UI when the user switches language.
        LocalizationManager.LanguageChanged += OnUILanguageChanged;
    }

    private void OnUILanguageChanged()
    {
        _menuBuilder.BuildMenu(this, _menuStrip);
        _menuBuilder.SetPluginsMenuVisible(Program.EnablePlugins);
        _menuBuilder.RefreshRecentFilesMenu(this);
        _menuBuilder.UpdateMenuState(this);
        _menuBuilder.RefreshEncodingMenu(this);
        _statusBarManager.RefreshLabels();
        UpdateTitleBar();
        ApplyLocalizedMenuTexts();
    }

    // ── Public properties ────────────────────────────────────────────

    /// <summary>Gets the list of all open tabs.</summary>
    public IReadOnlyList<TabInfo> Tabs => _tabs.AsReadOnly();

    /// <summary>Gets the index of the currently active tab, or -1 if none.</summary>
    public int ActiveTabIndex => _activeTabIndex;

    /// <summary>Gets the currently active tab, or null if none.</summary>
    public TabInfo? ActiveTab => _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count
        ? _tabs[_activeTabIndex]
        : null;

    public bool CanToggleWordWrap => ActiveTab is not null && IsWordWrapAllowed(ActiveTab);

    /// <summary>Gets the tab strip control.</summary>
    internal TabStrip TabStrip => _tabStrip;

    /// <summary>Gets the status bar manager.</summary>
    internal StatusBarManager StatusBarManager => _statusBarManager;

    /// <summary>Gets the recent files manager.</summary>
    internal RecentFilesManager RecentFilesManager => _recentFilesManager;

    /// <summary>Gets the plugin host.</summary>
    internal PluginHost PluginHost => _pluginHost;

    /// <summary>Gets the keyboard shortcut manager.</summary>
    internal KeyboardShortcutManager ShortcutManager => _shortcutManager;

    /// <summary>Gets the menu strip.</summary>
    internal MenuStrip MainMenu => _menuStrip;

    /// <summary>Gets the side panel container.</summary>
    internal Panel SidePanel => _sidePanel;

    /// <summary>Gets the bottom panel container.</summary>
    internal Panel BottomPanel => _bottomPanel;

    /// <summary>Gets or sets whether the side panel is visible.</summary>
    public bool IsSidePanelVisible
    {
        get => !_verticalSplit.Panel1Collapsed;
        set => _verticalSplit.Panel1Collapsed = !value;
    }

    /// <summary>Gets or sets whether the bottom panel is visible.</summary>
    public bool IsBottomPanelVisible
    {
        get => !_horizontalSplit.Panel2Collapsed;
        set => _horizontalSplit.Panel2Collapsed = !value;
    }

    // ── Document management ──────────────────────────────────────────

    /// <summary>
    /// Creates a new untitled document in a new tab.
    /// </summary>
    public void NewDocument()
    {
        _untitledCounter++;
        string title = string.Format(Strings.UntitledDocument, _untitledCounter);

        var pieceTable = new PieceTable(string.Empty);
        var editor = new EditorControl(pieceTable)
        {
            Theme = ThemeManager.Instance.CurrentTheme
        };
        if (EditorControl.DefaultWordWrap)
            editor.WordWrap = true;
        WireEditorEvents(editor);

        var tab = new TabInfo
        {
            Title = title,
            FilePath = null,
            IsModified = false,
            Editor = editor,
        };

        AddTab(tab);
        _pluginHost.RaiseDocumentOpened(string.Empty);
    }

    /// <summary>
    /// Opens a file from disk. If path is null, shows an Open File dialog.
    /// If the file is already open, activates its tab.
    /// </summary>
    public void OpenFile(string? path = null)
    {
        if (path is null)
        {
            using var dialog = new OpenFileDialog
            {
                Title = Strings.MenuOpen,
                Filter = BuildFileFilter(),
                Multiselect = true,
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            foreach (string file in dialog.FileNames)
                OpenFile(file);

            return;
        }

        // Check if already open.
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (string.Equals(_tabs[i].FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                ActivateTab(i);
                return;
            }
        }

        if (!File.Exists(path))
        {
            MessageBox.Show(
                string.Format(Strings.ErrorFileNotFound, path),
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        try
        {
            long fileSize = new FileInfo(path).Length;
            System.Text.Encoding? forcedBinaryTextEncoding = null;

            // Binary detection: read only the first 8 KB instead of the whole file.
            if (IsBinaryFileFromStream(path))
            {
                string? mode = ResolveBinaryOpenMode(path);
                if (mode is null) return; // cancelled
                if (mode == "hex")
                {
                    if (fileSize > 100 * 1024 * 1024)
                    {
                        MessageBox.Show(this,
                            string.Format(Strings.BinaryFileTooLargeForHex, fileSize / (1024 * 1024)),
                            Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    byte[] rawBytes = File.ReadAllBytes(path);
                    OpenBinaryFile(path, rawBytes);
                    return;
                }
                // mode == "text": fall through with forced Latin-1 encoding
                //                 to avoid misdetecting binary as Chinese/GB18030.
                forcedBinaryTextEncoding = System.Text.Encoding.GetEncoding(1252);
            }

            // ── Large file path: memory-mapped + async ──────────────────
            if (fileSize > LargeFileThreshold)
            {
                OpenLargeFile(path, fileSize, forcedBinaryTextEncoding);
                return;
            }

            // ── Small file path: existing sync pipeline ─────────────────
            byte[] smallRawBytes = File.ReadAllBytes(path);

            // Detect encoding (skip detection for known-binary opened as text).
            var encoding = forcedBinaryTextEncoding
                ?? EncodingDetector.DetectEncoding(smallRawBytes.AsSpan());
            byte[] preamble = encoding.GetPreamble();
            bool hasBom = forcedBinaryTextEncoding is null
                && preamble.Length > 0 && smallRawBytes.Length >= preamble.Length
                && smallRawBytes.AsSpan(0, preamble.Length).SequenceEqual(preamble);

            string text = encoding.GetString(smallRawBytes);
            // Strip BOM character if present.
            if (hasBom && text.Length > 0 && text[0] == '\uFEFF')
                text = text[1..];

            // Detect line ending style and normalize to \n internally (single pass).
            var (normalizedText, detectedLineEnding) = NormalizeLineEndings(text);

            var pieceTable = new PieceTable(normalizedText);
            var editor = new EditorControl(pieceTable)
            {
                FileSizeBytes = fileSize,
                Theme = ThemeManager.Instance.CurrentTheme,
                EncodingManager = new EncodingManager(encoding, hasBom),
                LineEnding = detectedLineEnding
            };
            WireEditorEvents(editor);

            // Detect language from file extension.
            string ext = Path.GetExtension(path);
            ILexer? lexer = LexerRegistry.Instance.GetLexerByExtension(ext);
            if (lexer is not null)
                editor.SetLexer(lexer);

            var tab = new TabInfo
            {
                Title = Path.GetFileName(path),
                FilePath = path,
                IsModified = false,
                Editor = editor,
            };

            AddTab(tab);
            _recentFilesManager.AddFile(path);
            _menuBuilder.RefreshRecentFilesMenu(this);
            _fileWatcher.Watch(tab);
            _pluginHost.RaiseDocumentOpened(path);
        }
        catch (UnauthorizedAccessException)
        {
            OfferAdminElevation(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(Strings.ErrorOpeningFile, path, ex.Message),
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Saves the currently active document. If untitled, calls SaveAs().
    /// </summary>
    public void SaveCurrentDocument()
    {
        TabInfo? tab = ActiveTab;
        if (tab is null) return;

        if (tab.FilePath is null)
        {
            SaveAs();
            return;
        }

        SaveDocument(tab);
    }

    /// <summary>
    /// Saves the current document to a new file path chosen by the user.
    /// </summary>
    public void SaveAs()
    {
        TabInfo? tab = ActiveTab;
        if (tab is null) return;

        using var dialog = new SaveFileDialog
        {
            Title = Strings.MenuSaveAs,
            Filter = BuildFileFilter(),
            FileName = tab.FilePath ?? tab.Title,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string oldPath = tab.FilePath ?? string.Empty;
        tab.FilePath = dialog.FileName;
        tab.Title = Path.GetFileName(dialog.FileName);

        // Detect language for the new extension only when no custom profile is active.
        if (tab.SelectedCustomProfileName is null)
        {
            string ext = Path.GetExtension(dialog.FileName);
            ILexer? lexer = LexerRegistry.Instance.GetLexerByExtension(ext);
            if (lexer is not null)
                tab.Editor.SetLexer(lexer);
        }

        SaveDocument(tab);

        _recentFilesManager.AddFile(dialog.FileName);
        _menuBuilder.RefreshRecentFilesMenu(this);

        if (!string.IsNullOrEmpty(oldPath))
            _fileWatcher.Unwatch(oldPath);

        _fileWatcher.Watch(tab);
    }

    /// <summary>
    /// Saves all open documents that have been modified.
    /// </summary>
    public void SaveAll()
    {
        foreach (TabInfo tab in _tabs)
        {
            if (tab.IsModified)
            {
                if (tab.FilePath is null)
                {
                    // Activate the tab so the user sees which file needs naming.
                    ActivateTab(_tabs.IndexOf(tab));
                    SaveAs();
                }
                else
                {
                    SaveDocument(tab);
                }
            }
        }
    }

    /// <summary>
    /// Closes the currently active document tab, prompting to save if modified.
    /// </summary>
    public bool CloseCurrentDocument()
    {
        if (ActiveTab is null) return true;
        return CloseTab(_activeTabIndex);
    }

    /// <summary>
    /// Activates the tab at the given index.
    /// </summary>
    public void ActivateTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;

        // Hide current editor and custom views.
        if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
        {
            var prevTab = _tabs[_activeTabIndex];
            prevTab.Editor.Visible = false;
            if (prevTab.Tag is DiffViewControl prevDiff)
                prevDiff.Visible = false;
            else if (prevTab.Tag is SedPreviewControl prevSed)
                prevSed.Visible = false;
        }

        _activeTabIndex = index;
        TabInfo tab = _tabs[index];

        // Ensure the editor is parented and sized before loading content.
        // The Document setter calls UpdateScrollBars() and Invalidate()
        // which need a properly sized surface to produce correct results.
        if (!_editorPanel.Controls.Contains(tab.Editor))
        {
            tab.Editor.Dock = DockStyle.Fill;
            _editorPanel.Controls.Add(tab.Editor);
        }

        // ── Deferred loading: load the file on first activation ───
        if (tab.IsDeferredLoad && tab.FilePath is not null)
        {
            LoadDeferredTab(tab);

            // LoadDeferredTab may replace this tab (large/binary files):
            // it removes the placeholder from _tabs and calls OpenFile,
            // which creates a new tab and activates it recursively.
            // If so, bail out — the new tab is already active.
            if (!_tabs.Contains(tab))
            {
                // Remove the placeholder editor we just parented above.
                if (_editorPanel.Controls.Contains(tab.Editor))
                    _editorPanel.Controls.Remove(tab.Editor);
                return;
            }
        }

        // If this is a diff tab, show the diff view instead of the editor.
        if (tab.Tag is DiffViewControl diffView)
        {
            tab.Editor.Visible = false;
            if (!_editorPanel.Controls.Contains(diffView))
            {
                diffView.Dock = DockStyle.Fill;
                _editorPanel.Controls.Add(diffView);
            }
            diffView.Visible = true;
            diffView.BringToFront();
            diffView.Focus();
        }
        else if (tab.Tag is SedPreviewControl sedView)
        {
            tab.Editor.Visible = false;
            if (!_editorPanel.Controls.Contains(sedView))
            {
                sedView.Dock = DockStyle.Fill;
                _editorPanel.Controls.Add(sedView);
            }
            sedView.Visible = true;
            sedView.BringToFront();
            sedView.Focus();
        }
        else
        {
            tab.Editor.Visible = true;
            tab.Editor.BringToFront();
            tab.Editor.Focus();
        }

        // ── Apply pending state (zoom/scroll/caret from session) ──
        ApplyPendingTabState(tab);

        // Force a full refresh after the message pump processes pending
        // layout.  Without this, a deferred tab shown for the first time
        // may render blank because WinForms hasn't finished sizing.
        BeginInvoke(() => tab.Editor.ActivateAndRefresh());

        _tabStrip.SetActiveTab(index);
        UpdateTitleBar();
        UpdateStatusBar();
        _menuBuilder.UpdateMenuState(this);
        _menuBuilder.RefreshLanguageMenu(this);
        _menuBuilder.RefreshEncodingMenu(this);
        RefreshSymbolList(tab);
    }

    /// <summary>
    /// Navigates to the next tab (wraps around).
    /// </summary>
    public void NextTab()
    {
        if (_tabs.Count <= 1) return;
        int next = (_activeTabIndex + 1) % _tabs.Count;
        ActivateTab(next);
    }

    /// <summary>
    /// Navigates to the previous tab (wraps around).
    /// </summary>
    public void PreviousTab()
    {
        if (_tabs.Count <= 1) return;
        int prev = (_activeTabIndex - 1 + _tabs.Count) % _tabs.Count;
        ActivateTab(prev);
    }

    /// <summary>
    /// Toggles full-screen mode.
    /// </summary>
    public void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            FormBorderStyle = _previousBorderStyle;
            WindowState = _previousWindowState;
            _menuStrip.Visible = true;
            _statusStrip.Visible = true;
            _isFullScreen = false;
        }
        else
        {
            _previousWindowState = WindowState;
            _previousBorderStyle = FormBorderStyle;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _menuStrip.Visible = false;
            _statusStrip.Visible = false;
            _isFullScreen = true;
        }
    }

    /// <summary>
    /// Shows the Find panel in the active editor.
    /// </summary>
    public void ShowFind()
    {
        ActiveTab?.Editor.ShowFindPanel(replaceMode: false);
    }

    /// <summary>
    /// Shows the Find and Replace panel in the active editor.
    /// </summary>
    public void ShowFindReplace()
    {
        ActiveTab?.Editor.ShowFindPanel(replaceMode: true);
    }

    /// <summary>
    /// Shows the Go to Line dialog.
    /// </summary>
    public void ShowGoToLine()
    {
        ActiveTab?.Editor.ShowGoToLineDialog();
    }

    /// <summary>
    /// Sets the language/lexer for the active document.
    /// </summary>
    public void SetLanguage(string languageId)
    {
        TabInfo? tab = ActiveTab;
        if (tab is null) return;

        if (string.Equals(languageId, "plaintext", StringComparison.OrdinalIgnoreCase))
        {
            tab.Editor.SetLexer(null);
        }
        else
        {
            ILexer? lexer = LexerRegistry.Instance.GetLexerById(languageId);
            if (lexer is not null)
                tab.Editor.SetLexer(lexer);
        }

        tab.SelectedCustomProfileName = null;
        tab.PendingCustomProfileName = null;

        UpdateStatusBar();
        _menuBuilder.RefreshLanguageMenu(this);
    }

    // ── Custom Highlighting ─────────────────────────────────────────

    /// <summary>The loaded custom highlight profiles.</summary>
    public IReadOnlyList<Editor.Highlighting.CustomHighlightProfile> CustomHighlightProfiles
        => _customHighlightStore.Profiles;

    /// <summary>Activates a custom highlight profile by name on the active tab.</summary>
    public void SetCustomHighlightProfile(string name)
    {
        TabInfo? tab = ActiveTab;
        if (tab is null) return;

        var profile = _customHighlightStore.FindByName(name);
        if (profile is not null)
        {
            tab.Editor.SetCustomHighlighting(profile);
            tab.SelectedCustomProfileName = profile.Name;
            tab.PendingCustomProfileName = profile.Name;
            tab.PendingLanguage = null;
        }

        UpdateStatusBar();
        _menuBuilder.RefreshLanguageMenu(this);
    }

    /// <summary>Opens the custom highlighting manager dialog.</summary>
    public void ShowCustomHighlightManager()
    {
        using var dialog = new CustomHighlightManagerDialog(
            _customHighlightStore,
            ThemeManager.Instance.CurrentTheme);

        dialog.ShowDialog(this);

        // Refresh active tab if it uses a custom profile.
        TabInfo? tab = ActiveTab;
        if (tab?.Editor.CustomProfileName is not null)
        {
            var profile = _customHighlightStore.FindByName(tab.Editor.CustomProfileName);
            tab.Editor.SetCustomHighlighting(profile); // null clears it if deleted
            tab.SelectedCustomProfileName = tab.Editor.CustomProfileName;
            tab.PendingCustomProfileName = tab.Editor.CustomProfileName;
            if (tab.Editor.CustomProfileName is not null)
                tab.PendingLanguage = null;
        }

        UpdateStatusBar();
        _menuBuilder.RefreshLanguageMenu(this);
    }

    /// <summary>
    /// Sets the encoding for the active document.
    /// </summary>
    public void SetEncoding(Encoding encoding, bool hasBom)
    {
        TabInfo? tab = ActiveTab;
        if (tab is null) return;

        tab.Editor.EncodingManager = new EncodingManager(encoding, hasBom);
        tab.IsModified = true;
        RefreshTabDisplay(tab);
        UpdateStatusBar();
        _menuBuilder.RefreshEncodingMenu(this);
    }

    /// <summary>
    /// Sets the line ending mode for the active document.
    /// </summary>
    public void SetLineEnding(string lineEnding)
    {
        ActiveTab?.Editor.SetLineEnding(lineEnding);
        UpdateStatusBar();
        _menuBuilder.RefreshEncodingMenu(this);
    }

    // ── Text transformations ────────────────────────────────────────

    /// <summary>
    /// Applies a text transformation to the selected text in the active editor.
    /// </summary>
    public void TransformSelection(Func<string, string> transform)
        => ActiveTab?.Editor.TransformSelection(transform);

    /// <summary>
    /// Applies a text transformation to the entire document.
    /// </summary>
    public void TransformDocument(Func<string, string> transform)
        => ActiveTab?.Editor.TransformDocument(transform);

    /// <summary>
    /// Applies a transformation to the selection if present, otherwise to the entire document.
    /// </summary>
    public void TransformSelectionOrDocument(Func<string, string> transform)
    {
        var editor = ActiveTab?.Editor;
        if (editor is null) return;

        if (editor.SelectionLength > 0)
            editor.TransformSelection(transform);
        else
            editor.TransformDocument(transform);
    }

    /// <summary>
    /// Toggles word wrap in the active editor.
    /// </summary>
    public void ToggleWordWrap()
    {
        var tab = ActiveTab;
        if (tab is null) return;

        if (!tab.Editor.WordWrap)
        {
            if (!IsWordWrapAllowed(tab))
                return;

            tab.Editor.WordWrap = true;
            SettingsManager.SetBool(SettingsManager.KeyWordWrap, true);
        }
        else
        {
            tab.Editor.WordWrap = false;
            SettingsManager.SetBool(SettingsManager.KeyWordWrap, false);
        }

        _menuBuilder.UpdateMenuState(this);
    }

    /// <summary>
    /// Toggles showing whitespace characters.
    /// </summary>
    public void ToggleShowWhitespace()
    {
        if (ActiveTab is not null)
        {
            ActiveTab.Editor.ShowWhitespace = !ActiveTab.Editor.ShowWhitespace;
            SettingsManager.SetBool(SettingsManager.KeyShowWhitespace, ActiveTab.Editor.ShowWhitespace);
            _menuBuilder.UpdateMenuState(this);
        }
    }

    /// <summary>
    /// Toggles line number display.
    /// </summary>
    public void ToggleLineNumbers()
    {
        if (ActiveTab is not null)
        {
            ActiveTab.Editor.ShowLineNumbers = !ActiveTab.Editor.ShowLineNumbers;
            SettingsManager.SetBool(SettingsManager.KeyShowLineNumbers, ActiveTab.Editor.ShowLineNumbers);
            _menuBuilder.UpdateMenuState(this);
        }
    }

    /// <summary>
    /// Changes zoom level in the active editor.
    /// </summary>
    public void ZoomIn() => ActiveTab?.Editor.ZoomIn();
    public void ZoomOut() => ActiveTab?.Editor.ZoomOut();
    public void ResetZoom() => ActiveTab?.Editor.ResetZoom();

    public void ToggleFoldAtCaret() => ActiveTab?.Editor.ToggleFoldAtCaret();
    public void FoldAll() => ActiveTab?.Editor.FoldAll();
    public void UnfoldAll() => ActiveTab?.Editor.UnfoldAll();

    /// <summary>
    /// Toggles the symbol list side panel.
    /// </summary>
    public void ToggleSymbolList()
    {
        IsSidePanelVisible = !IsSidePanelVisible;
    }

    /// <summary>
    /// Toggles the find results bottom panel.
    /// </summary>
    public void ToggleFindResults()
    {
        if (IsBottomPanelVisible && IsFindResultsTabActive)
        {
            IsBottomPanelVisible = false;
        }
        else
        {
            EnsureFindResultsTab();
            _bottomTabStrip.SelectTab("findResults");
            ShowSelectedBottomTab();
            IsBottomPanelVisible = true;
        }
    }

    private void EnsureFindResultsTab()
    {
        if (!_bottomTabStrip.HasTab("findResults"))
        {
            _bottomTabStrip.AddTab(new BottomPanelTab
            {
                Id = "findResults",
                Title = Strings.FindResultsHeader,
                Content = _findResultsPanel,
                Closable = true,
            });
            _findResultsPanel.Visible = false;
        }
    }

    /// <summary>
    /// Toggles the terminal panel. Creates the terminal tab on first call.
    /// </summary>
    public void ToggleTerminal()
    {
        if (IsBottomPanelVisible && IsTerminalTabActive)
        {
            IsBottomPanelVisible = false;
        }
        else
        {
            EnsureTerminalTab();
            _bottomTabStrip.SelectTab("terminal");
            ShowSelectedBottomTab();
            IsBottomPanelVisible = true;
        }
    }

    /// <summary>Whether the Find Results tab is currently selected in the bottom panel.</summary>
    public bool IsFindResultsTabActive => _bottomTabStrip.SelectedTabId == "findResults";

    /// <summary>Whether the Terminal tab is currently selected in the bottom panel.</summary>
    public bool IsTerminalTabActive => _bottomTabStrip.SelectedTabId == "terminal";

    private void EnsureTerminalTab()
    {
        if (_bottomTabStrip.HasTab("terminal")) return;

        _terminalPanel = new TerminalPanel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            Theme = ThemeManager.Instance.CurrentTheme
        };
        _bottomContentPanel.Controls.Add(_terminalPanel);

        _bottomTabStrip.AddTab(new BottomPanelTab
        {
            Id = "terminal",
            Title = Strings.MenuTerminal.Replace("&", ""),
            Content = _terminalPanel,
            Closable = true,
        });

        // Start in the current document's folder if available.
        string? dir = ActiveTab?.FilePath is not null
            ? Path.GetDirectoryName(ActiveTab.FilePath)
            : null;
        _terminalPanel.Start(dir);
    }

    private void OnBottomTabSelected(object? sender, BottomTabEventArgs e)
    {
        ShowSelectedBottomTab();
        if (e.TabId == "terminal")
            _terminalPanel?.FocusTerminal();
        _menuBuilder.UpdateMenuState(this);
    }

    private void OnBottomTabCloseRequested(object? sender, BottomTabEventArgs e)
    {
        if (e.TabId == "terminal" && _terminalPanel is not null)
        {
            _terminalPanel.Stop();
            _bottomContentPanel.Controls.Remove(_terminalPanel);
            _terminalPanel.Dispose();
            _terminalPanel = null;
            _bottomTabStrip.RemoveTab("terminal");
        }
        else if (e.TabId == "findResults")
        {
            _findResultsPanel.Visible = false;
            _bottomTabStrip.RemoveTab("findResults");
        }

        // If no tabs remain, collapse the panel
        if (!_bottomTabStrip.HasTab("findResults") && !_bottomTabStrip.HasTab("terminal"))
        {
            IsBottomPanelVisible = false;
        }
        else
        {
            ShowSelectedBottomTab();
        }
        _menuBuilder.UpdateMenuState(this);
    }

    private void ShowSelectedBottomTab()
    {
        _findResultsPanel.Visible = _bottomTabStrip.SelectedTabId == "findResults";
        _terminalPanel?.Visible = _bottomTabStrip.SelectedTabId == "terminal";
    }

    /// <summary>
    /// Populates the find results panel with search results and shows it.
    /// </summary>
    public void ShowFindAllResults(string searchPattern, List<Core.Search.SearchResult> results)
    {
        _findResultsPanel.Theme = ThemeManager.Instance.CurrentTheme;
        _findResultsPanel.AddSearchResults(results, searchPattern, Strings.ScopeCurrentDocument);
        EnsureFindResultsTab();
        _bottomTabStrip.SelectTab("findResults");
        ShowSelectedBottomTab();
        IsBottomPanelVisible = true;
        _menuBuilder.UpdateMenuState(this);
    }

    /// <summary>
    /// Handles navigation from a symbol list click to the editor.
    /// </summary>
    private void OnNavigateToSymbol(object? sender, SymbolNavigationEventArgs e)
    {
        if (ActiveTab is not null)
        {
            // LineNumber is one-based; GoToLine expects zero-based.
            ActiveTab.Editor.GoToLine(e.Symbol.LineNumber - 1);
            ActiveTab.Editor.Focus();
        }
    }

    /// <summary>
    /// Handles navigation from a find result click to the editor.
    /// </summary>
    private void OnNavigateToFindResult(object? sender, NavigateToResultEventArgs e)
    {
        var result = e.Result;

        // If the result is from a different file, open that file first.
        if (result.FilePath is not null)
        {
            // Check if file is already open.
            TabInfo? existingTab = _tabs.FirstOrDefault(
                t => string.Equals(t.FilePath, result.FilePath, StringComparison.OrdinalIgnoreCase));

            if (existingTab is not null)
            {
                int idx = _tabs.IndexOf(existingTab);
                ActivateTab(idx);
            }
            else
            {
                OpenFile(result.FilePath);
            }
        }

        // Navigate to the match in the active editor.
        if (ActiveTab is not null)
        {
            ActiveTab.Editor.Select(result.Offset, result.Length);
            ActiveTab.Editor.Focus();
        }
    }

    /// <summary>
    /// Handles the request to open find results as a new document tab.
    /// </summary>
    private void OnOpenFindResultsInNewTab(object? sender, string text)
    {
        _untitledCounter++;
        string title = $"Find Results {_untitledCounter}";

        var pieceTable = new PieceTable(text);
        var editor = new EditorControl(pieceTable)
        {
            Theme = ThemeManager.Instance.CurrentTheme
        };
        WireEditorEvents(editor);

        var tab = new TabInfo
        {
            Title = title,
            FilePath = null,
            IsModified = false,
            Editor = editor,
        };

        AddTab(tab);
    }

    /// <summary>
    /// Opens the hex editor for the current file.
    /// </summary>
    public void ToggleHexEditor()
    {
        TabInfo? tab = ActiveTab;
        if (tab is null) return;

        tab.Editor.IsHexPanelVisible = !tab.Editor.IsHexPanelVisible;
        _menuBuilder.UpdateMenuState(this);
    }

    /// <summary>
    /// Starts recording a macro.
    /// </summary>
    public void StartMacroRecording()
    {
        ActiveTab?.Editor.StartMacroRecording();
        _menuBuilder.UpdateMacroMenuState(true);
        _statusBarManager.SetMacroRecording(true);
    }

    /// <summary>
    /// Stops recording a macro.
    /// </summary>
    public void StopMacroRecording()
    {
        ActiveTab?.Editor.StopMacroRecording();
        _menuBuilder.UpdateMacroMenuState(false);
        _statusBarManager.SetMacroRecording(false);
    }

    /// <summary>
    /// Plays the last recorded macro.
    /// </summary>
    public void PlayMacro() => ActiveTab?.Editor.PlayMacro();

    /// <summary>
    /// Shows the macro manager dialog.
    /// </summary>
    public void ShowMacroManager() => ActiveTab?.Editor.ShowMacroManager();

    // ── Printing ──────────────────────────────────────────────────────

    private System.Drawing.Printing.PrintDocument? _printDoc;
    private string[]? _printLines;
    private int _printLineIndex;
    private string? _printTitle;

    private System.Drawing.Printing.PrintDocument CreatePrintDocument()
    {
        var tab = ActiveTab ?? throw new InvalidOperationException("No active tab.");
        string text = tab.Editor.Document.ToString();
        _printLines = text.Split('\n');
        _printLineIndex = 0;
        _printTitle = tab.Title;

        var doc = new System.Drawing.Printing.PrintDocument
        {
            DocumentName = _printTitle
        };
        doc.PrintPage += OnPrintPage;
        _printDoc = doc;
        return doc;
    }

    private void OnPrintPage(object sender, System.Drawing.Printing.PrintPageEventArgs e)
    {
        if (_printLines is null || e.Graphics is null) return;

        var font = new Font("Consolas", 10f);
        float lineHeight = font.GetHeight(e.Graphics);
        float x = e.MarginBounds.Left;
        float y = e.MarginBounds.Top;
        float maxY = e.MarginBounds.Bottom;

        // Print title as header on first page.
        if (_printLineIndex == 0 && _printTitle is not null)
        {
            using var headerFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            e.Graphics.DrawString(_printTitle, headerFont, Brushes.Black, x, y);
            y += headerFont.GetHeight(e.Graphics) + 8;
        }

        while (_printLineIndex < _printLines.Length)
        {
            if (y + lineHeight > maxY)
            {
                e.HasMorePages = true;
                return;
            }

            string line = _printLines[_printLineIndex].TrimEnd('\r');
            e.Graphics.DrawString(line, font, Brushes.Black, x, y);
            y += lineHeight;
            _printLineIndex++;
        }

        e.HasMorePages = false;
    }

    /// <summary>
    /// Opens the system print dialog for the active document.
    /// </summary>
    public void PrintDocument()
    {
        if (ActiveTab is null) return;

        try
        {
            var doc = CreatePrintDocument();
            using var dlg = new PrintDialog { Document = doc, UseEXDialog = true };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                doc.Print();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Opens a print preview window for the active document.
    /// </summary>
    public void PrintPreviewDocument()
    {
        if (ActiveTab is null) return;

        try
        {
            var doc = CreatePrintDocument();
            using var dlg = new PrintPreviewDialog
            {
                Document = doc,
                Width = 800,
                Height = 600,
                StartPosition = FormStartPosition.CenterParent,
            };
            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Shows the About dialog.
    /// </summary>
    public void ShowAbout()
    {
        using var dlg = new AboutForm(ThemeManager.Instance.CurrentTheme);
        dlg.ShowDialog(this);
    }

    /// <summary>
    /// Opens the %AppData%\Bascanka folder in Windows Explorer.
    /// </summary>
    public static void OpenAppDataFolder()
    {
        string path = SettingsManager.AppDataFolder;
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start("explorer.exe", path);
    }

    /// <summary>
    /// Shows the Settings dialog.
    /// </summary>
    public void ShowSettings()
    {
        using var dlg = new SettingsForm();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // Re-read all settings and apply to static properties and open editors.
        LoadAllSettings();

        // Read desired theme name before applying overrides, because the
        // ThemeChanged handler writes the *current* theme name back to the
        // registry, which would overwrite the user's choice.
        string themeName = SettingsManager.GetString(SettingsManager.KeyTheme, "Dark");
        ApplyThemeOverridesFromRegistry();
        try { ThemeManager.Instance.SetTheme(themeName); } catch { }

        // Apply to all open editors.
        foreach (var tab in _tabs)
            tab.Editor.ApplySettings();

        // Refresh tab strip with new dimensions.
        _tabStrip.Height = TabStrip.ConfigTabHeight;
        _tabStrip.Invalidate();

        // Refresh recent files menu in case display style changed.
        _menuBuilder.RefreshRecentFilesMenu(this);

        // Apply auto-save interval to the recovery timer.
        _recoveryManager.SetInterval(ConfigAutoSaveIntervalSeconds);
    }

    /// <summary>
    /// Reads theme colour overrides from the registry and applies them via ThemeManager.
    /// </summary>
    private static void ApplyThemeOverridesFromRegistry()
    {
        foreach (string themeName in ThemeManager.Instance.ThemeNames)
        {
            string? json = SettingsManager.GetThemeOverrides(themeName);
            if (json is null)
            {
                ThemeManager.Instance.ApplyOverrides(themeName, null);
                continue;
            }

            var overrides = ParseThemeOverridesJson(json);
            ThemeManager.Instance.ApplyOverrides(themeName, overrides.Count > 0 ? overrides : null);
        }
    }

    /// <summary>
    /// Parses a JSON string of theme colour overrides into a dictionary.
    /// </summary>
    private static Dictionary<string, Color> ParseThemeOverridesJson(string json)
    {
        var result = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    try
                    {
                        result[prop.Name] = ColorTranslator.FromHtml(prop.Value.GetString()!);
                    }
                    catch { }
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Reads all persisted settings from the registry and applies them to
    /// static properties on EditorControl, TabStrip, FindReplacePanel, etc.
    /// </summary>
    private static void LoadAllSettings()
    {
        // Editor
        EditorControl.DefaultFontFamily = SettingsManager.GetString(SettingsManager.KeyFontFamily, "Consolas");
        EditorControl.DefaultFontSize = SettingsManager.GetInt(SettingsManager.KeyFontSize, 11);
        EditorControl.DefaultTabWidth = SettingsManager.GetInt(SettingsManager.KeyTabWidth, 4);
        EditorControl.DefaultScrollSpeed = SettingsManager.GetInt(SettingsManager.KeyScrollSpeed, 3);
        EditorControl.DefaultAutoIndent = SettingsManager.GetBool(SettingsManager.KeyAutoIndent, true);
        EditorControl.DefaultWordWrap = SettingsManager.GetBool(SettingsManager.KeyWordWrap, false);
        EditorControl.DefaultCaretScrollBuffer = SettingsManager.GetInt(SettingsManager.KeyCaretScrollBuffer, 4);

        // Display
        EditorControl.DefaultCaretBlinkRate = SettingsManager.GetInt(SettingsManager.KeyCaretBlinkRate, 500);
        EditorControl.DefaultTextLeftPadding = SettingsManager.GetInt(SettingsManager.KeyTextLeftPadding, 6);
        EditorControl.DefaultLineSpacing = SettingsManager.GetInt(SettingsManager.KeyLineSpacing, 2);
        EditorControl.DefaultMinZoomFontSize = SettingsManager.GetInt(SettingsManager.KeyMinZoomFontSize, 6);
        EditorControl.DefaultWhitespaceOpacity = SettingsManager.GetInt(SettingsManager.KeyWhitespaceOpacity, 100);
        EditorControl.DefaultFoldIndicatorOpacity = SettingsManager.GetInt(SettingsManager.KeyFoldIndicatorOpacity, 60);
        EditorControl.DefaultGutterPaddingLeft = SettingsManager.GetInt(SettingsManager.KeyGutterPaddingLeft, 8);
        EditorControl.DefaultGutterPaddingRight = SettingsManager.GetInt(SettingsManager.KeyGutterPaddingRight, 12);
        EditorControl.DefaultFoldButtonSize = SettingsManager.GetInt(SettingsManager.KeyFoldButtonSize, 10);
        EditorControl.DefaultBookmarkSize = SettingsManager.GetInt(SettingsManager.KeyBookmarkSize, 8);
        TabStrip.ConfigTabHeight = SettingsManager.GetInt(SettingsManager.KeyTabHeight, 30);
        ThemedMenuRenderer.ConfigMenuItemPadding = SettingsManager.GetInt(SettingsManager.KeyMenuItemPadding, ThemedMenuRenderer.DefaultMenuItemPadding);
        TerminalPanel.ConfigTerminalPadding = SettingsManager.GetInt(SettingsManager.KeyTerminalPadding, TerminalPanel.DefaultTerminalPadding);
        TabStrip.ConfigMaxTabWidth = SettingsManager.GetInt(SettingsManager.KeyMaxTabWidth, 220);
        TabStrip.ConfigMinTabWidth = SettingsManager.GetInt(SettingsManager.KeyMinTabWidth, 80);

        // Performance
        LargeFileThreshold = (long)SettingsManager.GetInt(SettingsManager.KeyLargeFileThresholdMB, 10) * 1024 * 1024;
        EditorControl.FoldingMaxFileSize = (long)SettingsManager.GetInt(SettingsManager.KeyFoldingMaxFileSizeMB, 50) * 1_000_000;
        WordWrapMaxFileSizeBytes = (long)SettingsManager.GetInt(SettingsManager.KeyWordWrapMaxFileSizeMB, 50) * 1024 * 1024;
        RecentFilesManager.MaxRecentFiles = SettingsManager.GetInt(SettingsManager.KeyMaxRecentFiles, 20);
        FindReplacePanel.ConfigMaxHistoryItems = SettingsManager.GetInt(SettingsManager.KeySearchHistoryLimit, 25);
        EditorControl.DefaultSearchDebounce = SettingsManager.GetInt(SettingsManager.KeySearchDebounce, 300);
        ConfigAutoSaveIntervalSeconds = SettingsManager.GetInt(SettingsManager.KeyAutoSaveInterval, RecoveryManager.DefaultIntervalSeconds);
    }

    /// <summary>
    /// Shows a command palette-style dialog (placeholder for future implementation).
    /// </summary>
    public static void ShowCommandPalette()
    {
        // Future: implement a VS Code-style command palette.
        MessageBox.Show(
            Strings.CommandPaletteNotYetImplemented,
            Strings.AppTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// Reloads the active document from disk.
    /// </summary>
    public async void ReloadActiveDocument()
    {
        TabInfo? tab = ActiveTab;
        if (tab?.FilePath is null) return;

        try
        {
            long fileSize = new FileInfo(tab.FilePath).Length;

            // ── Large file path: incremental async reload ───────────
            if (fileSize > LargeFileThreshold)
            {
                string filePath = tab.FilePath;
                tab.Editor.IsReadOnly = true;

                var source = await Task.Run(() =>
                    new MemoryMappedFileSource(filePath, normalizeLineEndings: true, deferScan: true));

                const int FirstBatchChunks = 128;
                const int SubsequentBatchChunks = 2048;
                int batchSize = FirstBatchChunks;
                bool done = false;

                while (!done)
                {
                    done = await Task.Run(() => source.ScanNextBatch(batchSize));

                    if (!_tabs.Contains(tab))
                    {
                        source.Dispose();
                        return;
                    }

                    long savedScroll = tab.Editor.ScrollMgr.FirstVisibleLine;
                    long savedCaret = tab.Editor.CaretOffset;
                    var selMgr = tab.Editor.SelectionMgr;
                    bool hadSelection = selMgr.HasSelection;
                    long savedSelStart = selMgr.SelectionStart;
                    long savedSelEnd = selMgr.SelectionEnd;

                    ITextSource textSource = done
                        ? source
                        : new BorrowedTextSource(source);

                    // Suppress painting during document swap to avoid blinking.
                    SendMessage(tab.Editor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                    try
                    {
                        tab.Editor.Document = new PieceTable(textSource);

                        // Restore caret BEFORE scroll so that the scroll position
                        // the user is actually looking at wins over EnsureVisible.
                        long docLen = tab.Editor.Document.Length;
                        if (hadSelection && savedSelStart < docLen)
                        {
                            long clampedEnd = Math.Min(savedSelEnd, docLen);
                            tab.Editor.Select(savedSelStart, (int)(clampedEnd - savedSelStart));
                        }
                        else if (savedCaret > 0 && savedCaret <= docLen)
                        {
                            tab.Editor.CaretOffset = savedCaret;
                        }

                        tab.Editor.ScrollMgr.ScrollToLine(savedScroll);
                    }
                    finally
                    {
                        SendMessage(tab.Editor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                        tab.Editor.Invalidate(true);
                    }

                    tab.Editor.FileSizeBytes = source.ScannedBytes;
                    UpdateStatusBar();

                    if (!done)
                        batchSize = SubsequentBatchChunks;
                }

                tab.Editor.FileSizeBytes = fileSize;
                tab.Editor.EncodingManager = new EncodingManager(source.Encoding,
                    source.Encoding.GetPreamble().Length > 0);
                tab.Editor.LineEnding = source.DetectedLineEnding;
                tab.Editor.IsMemoryMappedDocument = true;
                tab.Editor.IsReadOnly = false;
                tab.IsModified = false;
                RefreshTabDisplay(tab);
                UpdateStatusBar();
                _menuBuilder.RefreshEncodingMenu(this);
                return;
            }

            // ── Small file path: existing sync reload ───────────────
            byte[] rawBytes = File.ReadAllBytes(tab.FilePath);
            var encoding = EncodingDetector.DetectEncoding(rawBytes.AsSpan());
            byte[] preamble = encoding.GetPreamble();
            bool hasBom = preamble.Length > 0 && rawBytes.Length >= preamble.Length
                && rawBytes.AsSpan(0, preamble.Length).SequenceEqual(preamble);

            string text = encoding.GetString(rawBytes);
            if (hasBom && text.Length > 0 && text[0] == '\uFEFF')
                text = text[1..];

            // Normalize line endings on reload (single pass).
            var (normalizedText, detectedLineEnding) = NormalizeLineEndings(text);

            tab.Editor.FileSizeBytes = fileSize;
            tab.Editor.ReloadContent(normalizedText);
            tab.Editor.EncodingManager = new EncodingManager(encoding, hasBom);
            tab.Editor.LineEnding = detectedLineEnding;
            tab.IsModified = false;
            RefreshTabDisplay(tab);
            UpdateStatusBar();
            _menuBuilder.RefreshEncodingMenu(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(Strings.ErrorOpeningFile, tab.FilePath, ex.Message),
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Updates the status bar to reflect the current editor state.
    /// </summary>
    public void UpdateStatusBar()
    {
        TabInfo? tab = ActiveTab;
        if (tab is null)
        {
            _statusBarManager.Clear();
            return;
        }

        _statusBarManager.Update(tab);
    }

    // ── Overrides ────────────────────────────────────────────────────

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Apply theme to the form.
        ApplyTheme(ThemeManager.Instance.CurrentTheme);
        ThemeManager.Instance.ThemeChanged += (_, _) =>
        {
            ApplyTheme(ThemeManager.Instance.CurrentTheme);
            SettingsManager.SetString(SettingsManager.KeyTheme, ThemeManager.Instance.CurrentTheme.Name);
        };

        // Restore previous session from the recovery manifest, then open any command-line files on top.
        if (!_recoveryManager.RestoreFromRecovery() && _initialFiles.Length == 0)
        {
            // No session to restore and no files to open; create a new empty document.
            NewDocument();
        }

        // Open files from command line (on top of the restored session).
        foreach (string file in _initialFiles)
            OpenFile(file);

        _recoveryManager.SetInterval(ConfigAutoSaveIntervalSeconds);
        _recoveryManager.Start();

        // Load plugins.
        _pluginHost.LoadPlugins();

        // Start listening for files from other instances.
        _singleInstance?.StartListening(OnExternalFilesReceived);
    }

    /// <summary>
    /// Called on a background thread when another Bascanka instance sends file paths.
    /// Marshals to the UI thread, opens each file, and brings the window to the foreground.
    /// </summary>
    private void OnExternalFilesReceived(string[] files)
    {
        BeginInvoke(() =>
        {
            foreach (string file in files)
                OpenFile(file);

            // Bring window to foreground.
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            SetForegroundWindow(Handle);
            Activate();
        });
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SETREDRAW = 0x000B;
    private const int WM_CUT = 0x0300;
    private const int WM_COPY = 0x0301;
    private const int WM_PASTE = 0x0302;
    private const int EM_SETSEL = 0x00B1;
    private const int WM_UNDO = 0x0304;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        // Stop the recovery timer — we'll do a final write below.
        _recoveryManager.Stop();

        // Force a final snapshot so the manifest stays up to date and dirty tabs survive exit.
        foreach (var tab in _tabs)
        {
            if (tab.IsModified)
                _recoveryManager.MarkDirty(tab.Id);
        }
        _recoveryManager.ForceWrite();
        _recoveryManager.Dispose();

        // Stop terminal process.
        _terminalPanel?.Stop();

        // Shutdown plugins.
        _pluginHost.Shutdown();
        _fileWatcher.Dispose();
        _singleInstance?.Dispose();
    }

    /// <summary>
    /// Intercepts command keys before the menu system processes them.
    /// When a TextBox or ComboBox has focus, standard edit shortcuts
    /// (Ctrl+V/C/X/A/Z) are sent directly to the focused native window
    /// instead of being routed to the Edit menu handlers.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys modifiers = keyData & Keys.Modifiers;
        Keys key = keyData & Keys.KeyCode;

        if (modifiers == Keys.Control)
        {
            IntPtr focusedHwnd = GetFocus();
            if (focusedHwnd != IntPtr.Zero && IsTextInputFocused())
            {
                int wmMsg = key switch
                {
                    Keys.V => WM_PASTE,
                    Keys.C => WM_COPY,
                    Keys.X => WM_CUT,
                    Keys.Z => WM_UNDO,
                    Keys.A => EM_SETSEL,
                    _ => 0,
                };

                if (wmMsg != 0)
                {
                    if (wmMsg == EM_SETSEL)
                        SendMessage(focusedHwnd, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                    else
                        SendMessage(focusedHwnd, wmMsg, IntPtr.Zero, IntPtr.Zero);

                    return true; // Key handled — do not pass to menu system.
                }
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Forward to shortcut manager first.
        if (_shortcutManager.ProcessShortcut(e.KeyCode, e.Control, e.Shift, e.Alt))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Forward to plugins.
        var pluginArgs = new Plugins.Api.KeyEventArgs2(
            (int)e.KeyCode, e.Control, e.Shift, e.Alt);
        _pluginHost.RaiseKeyDown(pluginArgs);
        if (pluginArgs.Handled)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (string file in files)
                OpenFile(file);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────

    private void AddTab(TabInfo tab)
    {
        int insertAt = _deferredInsertIndex;
        _deferredInsertIndex = -1;

        if (insertAt >= 0 && insertAt <= _tabs.Count)
        {
            _tabs.Insert(insertAt, tab);
            _tabStrip.InsertTab(insertAt, tab);
            ActivateTab(insertAt);
        }
        else
        {
            _tabs.Add(tab);
            _tabStrip.AddTab(tab);
            ActivateTab(_tabs.Count - 1);
        }
    }

    /// <summary>
    /// Creates a lightweight placeholder tab for session restore.
    /// The file is not loaded until the tab is activated.
    /// </summary>
    public void AddDeferredTab(string path, int zoom, int scroll, int caret, bool wordWrap = false)
    {
        var editor = new EditorControl
        {
            Theme = ThemeManager.Instance.CurrentTheme
        };

        var tab = new TabInfo
        {
            Title = Path.GetFileName(path),
            FilePath = path,
            IsModified = false,
            Editor = editor,
            IsDeferredLoad = true,
            PendingZoom = zoom,
            PendingScroll = scroll,
            PendingCaret = caret,
            PendingWordWrap = wordWrap ? true : null,
        };

        _tabs.Add(tab);
        _tabStrip.AddTab(tab, select: false);
    }

    /// <summary>
    /// Wires editor events and adds a recovered tab with pending state.
    /// Used by <see cref="RecoveryManager"/> during crash recovery restore.
    /// </summary>
    internal void WireAndAddRecoveredTab(TabInfo tab, long caret, int scroll, int zoom)
    {
        WireEditorEvents(tab.Editor);

        tab.PendingCaret = caret;
        tab.PendingScroll = scroll;
        tab.PendingZoom = zoom;

        _tabs.Add(tab);
        _tabStrip.AddTab(tab, select: false);

        if (tab.FilePath is not null)
            _fileWatcher.Watch(tab);
    }

    /// <summary>
    /// Creates a deferred tab for a modified large file from crash recovery.
    /// The expensive async MMF scanning is delayed until the tab is activated.
    /// </summary>
    internal void AddDeferredRecoveryLargeTab(
        string path, string addBuffer, IReadOnlyList<Piece> pieces,
        int codePage, bool hasBom, string? lineEnding, string? language,
        long savedCaret, int savedScroll, int savedZoom,
        Guid? recoveryTabId, bool wordWrap, string? customProfileName)
    {
        var editor = new EditorControl
        {
            Theme = ThemeManager.Instance.CurrentTheme
        };

        var tab = new TabInfo
        {
            Id = recoveryTabId ?? Guid.NewGuid(),
            Title = Path.GetFileName(path),
            FilePath = path,
            IsModified = true,
            Editor = editor,
            IsDeferredLoad = true,
            PendingZoom = savedZoom,
            PendingScroll = savedScroll,
            PendingCaret = savedCaret,
            PendingWordWrap = wordWrap ? true : null,
            PendingLanguage = language,
            PendingCustomProfileName = customProfileName,
            PendingRecoveryFormat = "pieces",
            PendingEncodingCodePage = codePage,
            PendingHasBom = hasBom,
            PendingLineEnding = lineEnding,
        };

        _deferredRecovery[path] = new DeferredRecoveryData(
            addBuffer, pieces, codePage, hasBom, lineEnding, language,
            savedCaret, savedScroll, savedZoom, recoveryTabId, wordWrap, customProfileName);

        _tabs.Add(tab);
        _tabStrip.AddTab(tab, select: false);
    }

    /// <summary>
    /// Asynchronously restores a large MMF-backed tab from recovery data.
    /// Creates a placeholder tab immediately, scans the file incrementally on a
    /// background thread, then reconstructs the piece table from recovery pieces.
    /// Same pattern as <see cref="OpenLargeFile"/>.
    /// </summary>
    internal async void RestoreLargeFileFromRecovery(
        string path, long fileSize,
        string addBuffer, IReadOnlyList<Piece> pieces,
        int codePage, bool hasBom, string? lineEnding, string? language,
        int savedZoom, Guid? recoveryTabId = null, bool wordWrap = false,
        string? customProfileName = null)
    {
        // 1. Create tab immediately with an empty document + read-only.
        var editor = new EditorControl(new PieceTable(string.Empty))
        {
            Theme = ThemeManager.Instance.CurrentTheme,
            IsReadOnly = true,
            IsMemoryMappedDocument = true // set early so recovery timer uses pieces format
        };
        WireEditorEvents(editor);

        string ext = Path.GetExtension(path);
        ILexer? lexer = null;
        if (!string.IsNullOrEmpty(language))
            lexer = LexerRegistry.Instance.GetLexerById(language);
        lexer ??= LexerRegistry.Instance.GetLexerByExtension(ext);
        if (lexer is not null)
            editor.SetLexer(lexer);

        var tab = new TabInfo
        {
            Id = recoveryTabId ?? Guid.NewGuid(),
            Title = Path.GetFileName(path),
            FilePath = path,
            IsModified = true,
            Editor = editor,
        };

        AddTab(tab);
        _fileWatcher.Watch(tab);
        tab.IsLoading = true;

        try
        {
            // 2. Create MMF source with deferred scanning.
            var source = await Task.Run(() =>
                new MemoryMappedFileSource(path, normalizeLineEndings: true, deferScan: true));

            // 3. Incremental scanning loop.
            //    Every batch builds a recovery PieceTable so the user's edit
            //    is visible throughout loading.  A single line-offset buffer is
            //    reused across batches (doubling strategy) to avoid per-batch
            //    LOH allocations that cause GC stalls.
            const int FirstBatchChunks = 128;
            const int SubsequentBatchChunks = 2048;
            int batchSize = FirstBatchChunks;
            bool done = false;
            long[]? recoveryOffsetBuffer = null; // reused across batches

            while (!done)
            {
                var (Done, Doc, ScannedBytes) = await Task.Run(() =>
                {
                    bool d = source.ScanNextBatch(batchSize);
                    long scannedCharLen = source.Length;

                    IReadOnlyList<Piece> safePieces = d
                        ? pieces
                        : RecoveryManager.FilterPiecesToScannedRange(
                            pieces, scannedCharLen, source);

                    ITextSource textSrc = d
                        ? (ITextSource)source
                        : new BorrowedTextSource(source);

                    var pt = new PieceTable(textSrc, addBuffer, safePieces);

                    long[]? srcOffsets = source.LineOffsets;
                    int validCount = 0;
                    if (srcOffsets is not null)
                    {
                        (recoveryOffsetBuffer, validCount) =
                            RecoveryManager.ComputeRecoveryLineOffsetsInto(
                                srcOffsets, addBuffer, safePieces, recoveryOffsetBuffer);
                        pt.SetLineOffsetCache(recoveryOffsetBuffer, validCount);
                    }
                    else
                    {
                        pt.PrecomputeLineOffsets();
                    }

                    return (Done: d, Doc: pt, source.ScannedBytes);
                });

                done = Done;

                if (!_tabs.Contains(tab))
                {
                    Doc.Dispose();
                    return;
                }

                long curScroll = tab.Editor.ScrollMgr.FirstVisibleLine;
                long curCaret = tab.Editor.CaretOffset;

                SendMessage(tab.Editor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                try
                {
                    tab.Editor.Document = Doc;

                    long docLen = tab.Editor.Document.Length;
                    if (curCaret > 0 && curCaret <= docLen)
                        tab.Editor.CaretOffset = curCaret;
                    tab.Editor.ScrollMgr.ScrollToLine(curScroll);
                }
                finally
                {
                    SendMessage(tab.Editor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    tab.Editor.Invalidate(true);
                }

                tab.Editor.FileSizeBytes = ScannedBytes;
                if (ActiveTab == tab)
                    _statusBarManager.ShowLoadingProgress(ScannedBytes, fileSize);

                if (!done)
                    batchSize = SubsequentBatchChunks;
            }

            // 5. Final setup.
            tab.Editor.FileSizeBytes = fileSize;

            System.Text.Encoding enc;
            try { enc = System.Text.Encoding.GetEncoding(codePage); }
            catch { enc = new UTF8Encoding(false); }
            tab.Editor.EncodingManager = new EncodingManager(enc, hasBom);

            if (!string.IsNullOrEmpty(lineEnding))
                tab.Editor.LineEnding = lineEnding;

            tab.Editor.IsMemoryMappedDocument = true;
            tab.Editor.IsReadOnly = false;
            tab.IsModified = true;
            tab.IsLoading = false;

            if (savedZoom != 0)
                tab.Editor.ZoomLevel = savedZoom;
            if (wordWrap && IsWordWrapAllowed(tab))
                tab.Editor.WordWrap = true;

            if (!string.IsNullOrEmpty(customProfileName))
            {
                var profile = _customHighlightStore.FindByName(customProfileName);
                if (profile is not null)
                    tab.Editor.SetCustomHighlighting(profile);
                else if (lexer is not null)
                    tab.Editor.SetLexer(lexer);
            }
            else if (lexer is not null)
            {
                tab.Editor.SetLexer(lexer);
            }

            RefreshTabDisplay(tab);
            UpdateTitleBar();
            if (ActiveTab == tab)
                UpdateStatusBar();
            _menuBuilder.RefreshEncodingMenu(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recovery large file restore failed: {ex.Message}");
            tab.Editor.IsReadOnly = false;
            tab.IsLoading = false;
        }
    }

    /// <summary>
    /// Loads the file content for a deferred tab.
    /// Reuses the existing EditorControl and replaces its document.
    /// </summary>
    private void LoadDeferredTab(TabInfo tab)
    {
        string path = tab.FilePath!;
        tab.IsDeferredLoad = false;

        if (!File.Exists(path))
            return;

        // Check for deferred recovery data (modified large file from crash recovery).
        if (_deferredRecovery.Remove(path, out var recovery))
        {
            long fileSize = new FileInfo(path).Length;
            int idx = _tabs.IndexOf(tab);
            if (idx >= 0)
            {
                _tabs.RemoveAt(idx);
                _tabStrip.RemoveTab(idx);
                _deferredInsertIndex = idx;
            }
            RestoreLargeFileFromRecovery(
                path, fileSize,
                recovery.AddBuffer, recovery.Pieces,
                recovery.CodePage, recovery.HasBom, recovery.LineEnding, recovery.Language,
                recovery.SavedZoom, recovery.RecoveryTabId, recovery.WordWrap,
                recovery.CustomProfileName);
            _deferredInsertIndex = -1;
            return;
        }

        try
        {
            long fileSize = new FileInfo(path).Length;

            if (IsBinaryFileFromStream(path) || fileSize > LargeFileThreshold)
            {
                // Binary / large files need specialised loading.
                // Remove the placeholder and use the normal OpenFile path,
                // preserving the original tab position.
                int idx = _tabs.IndexOf(tab);
                if (idx >= 0)
                {
                    _tabs.RemoveAt(idx);
                    _tabStrip.RemoveTab(idx);
                    _deferredInsertIndex = idx;
                }
                OpenFile(path);
                _deferredInsertIndex = -1;
                return;
            }

            byte[] smallRawBytes = File.ReadAllBytes(path);
            var encoding = EncodingDetector.DetectEncoding(smallRawBytes.AsSpan());
            byte[] preamble = encoding.GetPreamble();
            bool hasBom = preamble.Length > 0 && smallRawBytes.Length >= preamble.Length
                && smallRawBytes.AsSpan(0, preamble.Length).SequenceEqual(preamble);

            string text = encoding.GetString(smallRawBytes);
            if (hasBom && text.Length > 0 && text[0] == '\uFEFF')
                text = text[1..];

            var (normalizedText, detectedLineEnding) = NormalizeLineEndings(text);

            var pieceTable = new PieceTable(normalizedText);
            tab.Editor.Document = pieceTable;
            tab.Editor.FileSizeBytes = fileSize;
            tab.Editor.EncodingManager = new EncodingManager(encoding, hasBom);
            tab.Editor.LineEnding = detectedLineEnding;
            WireEditorEvents(tab.Editor);

            string ext = Path.GetExtension(path);
            if (tab.SelectedCustomProfileName is null)
            {
                ILexer? lexer = LexerRegistry.Instance.GetLexerByExtension(ext);
                if (lexer is not null)
                    tab.Editor.SetLexer(lexer);
            }

            _fileWatcher.Watch(tab);
        }
        catch (UnauthorizedAccessException)
        {
            OfferAdminElevation(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load deferred tab: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies and clears pending zoom/scroll/caret/language state on a tab.
    /// </summary>
    private void ApplyPendingTabState(TabInfo tab)
    {
        // Apply pending language or custom highlight profile (overrides extension detection).
        if (tab.PendingCustomProfileName is not null)
        {
            var profile = _customHighlightStore.FindByName(tab.PendingCustomProfileName);
            if (profile is not null)
            {
                tab.Editor.SetCustomHighlighting(profile);
                tab.SelectedCustomProfileName = profile.Name;
            }
            else
            {
                tab.SelectedCustomProfileName = null;
            }
            tab.PendingCustomProfileName = null;
            tab.PendingLanguage = null;
        }
        else if (tab.PendingLanguage is not null)
        {
            if (string.Equals(tab.PendingLanguage, "plaintext", StringComparison.OrdinalIgnoreCase))
                tab.Editor.SetLexer(null);
            else
            {
                var lexer = LexerRegistry.Instance.GetLexerById(tab.PendingLanguage);
                if (lexer is not null)
                    tab.Editor.SetLexer(lexer);
            }
            tab.SelectedCustomProfileName = null;
            tab.PendingLanguage = null;
        }

        if (tab.PendingZoom == 0 && tab.PendingScroll == 0 && tab.PendingCaret == 0
            && tab.PendingWordWrap is null)
            return;

        if (tab.PendingZoom != 0)
            tab.Editor.ZoomLevel = tab.PendingZoom;
        if (tab.PendingScroll > 0)
            tab.Editor.ScrollMgr.FirstVisibleLine = tab.PendingScroll;
        if (tab.PendingCaret > 0 && tab.PendingCaret <= tab.Editor.GetBufferLength())
            tab.Editor.CaretOffset = tab.PendingCaret;
        if (tab.PendingWordWrap == true && IsWordWrapAllowed(tab))
            tab.Editor.WordWrap = true;

        tab.PendingZoom = 0;
        tab.PendingScroll = 0;
        tab.PendingCaret = 0;
        tab.PendingWordWrap = null;
    }

    /// <summary>
    /// When a file cannot be opened due to access denied, offers to restart
    /// the application as Administrator with the file path as argument.
    /// Returns true if the user accepted and the elevated process was launched.
    /// </summary>
    private static bool OfferAdminElevation(string path)
    {
        var result = MessageBox.Show(
            string.Format(Strings.ErrorAccessDenied, path),
            Strings.RestartAsAdmin,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return false;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = $"\"{path}\"",
                Verb = "runas",
                UseShellExecute = true,
            };

            System.Diagnostics.Process.Start(psi);
            Application.Exit();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled the UAC prompt.
            return false;
        }
    }

    private static bool IsWordWrapAllowed(TabInfo tab)
    {
        if (WordWrapMaxFileSizeBytes <= 0)
            return true;

        long size = tab.Editor.FileSizeBytes;
        return size <= 0 || size <= WordWrapMaxFileSizeBytes;
    }

    /// <summary>
    /// Opens two file-open dialogs and compares the selected files in a diff tab.
    /// </summary>
    public void CompareFiles()
    {
        string filter = $"{Strings.FilterAllFiles} (*.*)|*.*";

        using var dlg1 = new OpenFileDialog
        {
            Title = Strings.CompareSelectFirstFile,
            Filter = filter,
        };
        if (dlg1.ShowDialog(this) != DialogResult.OK) return;

        using var dlg2 = new OpenFileDialog
        {
            Title = Strings.CompareSelectSecondFile,
            Filter = filter,
        };
        if (dlg2.ShowDialog(this) != DialogResult.OK) return;

        string text1 = File.ReadAllText(dlg1.FileName);
        string text2 = File.ReadAllText(dlg2.FileName);
        string name1 = Path.GetFileName(dlg1.FileName);
        string name2 = Path.GetFileName(dlg2.FileName);

        var result = DiffEngine.Compare(text1, text2, name1, name2);

        if (result.DiffCount == 0)
        {
            MessageBox.Show(this, Strings.DiffNoDifferences, Strings.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        OpenDiffTab(result, $"{name1} \u2194 {name2}");
    }

    private void OpenDiffTab(DiffResult result, string title)
    {
        var diffView = new DiffViewControl();
        diffView.LoadDiff(result);
        diffView.ApplyTheme(ThemeManager.Instance.CurrentTheme);

        var dummyEditor = new EditorControl { Visible = false };
        var tab = new TabInfo
        {
            Title = title,
            Editor = dummyEditor,
            Tag = diffView,
        };
        AddTab(tab);
    }

    // ── Tab context menu: Compare with ──────────────────────────────

    private void OnTabContextMenuOpening(object? sender, TabContextMenuOpeningEventArgs e)
    {
        var menu = e.Menu;
        int sourceIndex = e.Index;
        if (sourceIndex < 0 || sourceIndex >= _tabs.Count) return;

        // Remove previously added compare items (tagged).
        for (int i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i].Tag is string tag && tag == "compare")
                menu.Items.RemoveAt(i);
        }

        var sourceTab = _tabs[sourceIndex];

        // Build "Compare with..." submenu.
        var compareMenu = new ToolStripMenuItem(Strings.CompareWithTab) { Tag = "compare" };

        // Add an entry for each other open tab.
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (i == sourceIndex) continue;
            var otherTab = _tabs[i];
            // Skip diff/sed preview tabs (they have no real editor text).
            if (otherTab.Tag is DiffViewControl or SedPreviewControl) continue;

            string itemText = otherTab.DisplayTitle ?? otherTab.Title;
            int otherIndex = i;
            var item = new ToolStripMenuItem(itemText);
            item.Click += (_, _) => CompareTabWith(sourceTab, _tabs[otherIndex]);
            compareMenu.DropDownItems.Add(item);
        }

        // Separator + Browse option (compare with file on disk).
        if (compareMenu.DropDownItems.Count > 0)
            compareMenu.DropDownItems.Add(new ToolStripSeparator());

        var browseItem = new ToolStripMenuItem(Strings.CompareWithBrowse);
        browseItem.Click += (_, _) => CompareTabWithFile(sourceTab);
        compareMenu.DropDownItems.Add(browseItem);

        // Insert before the last separator in the context menu (before Copy Path).
        menu.Items.Add(new ToolStripSeparator { Tag = "compare" });
        menu.Items.Add(compareMenu);
    }

    private void CompareTabWith(TabInfo sourceTab, TabInfo otherTab)
    {
        string text1 = sourceTab.Editor.GetAllText();
        string text2 = otherTab.Editor.GetAllText();
        string name1 = sourceTab.DisplayTitle ?? sourceTab.Title;
        string name2 = otherTab.DisplayTitle ?? otherTab.Title;

        var result = DiffEngine.Compare(text1, text2, name1, name2);

        if (result.DiffCount == 0)
        {
            MessageBox.Show(this, Strings.DiffNoDifferences, Strings.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        OpenDiffTab(result, $"{name1} \u2194 {name2}");
    }

    private void CompareTabWithFile(TabInfo sourceTab)
    {
        string filter = $"{Strings.FilterAllFiles} (*.*)|*.*";
        using var dlg = new OpenFileDialog
        {
            Title = Strings.CompareSelectSecondFile,
            Filter = filter,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string text1 = sourceTab.Editor.GetAllText();
        string text2 = File.ReadAllText(dlg.FileName);
        string name1 = sourceTab.DisplayTitle ?? sourceTab.Title;
        string name2 = Path.GetFileName(dlg.FileName);

        var result = DiffEngine.Compare(text1, text2, name1, name2);

        if (result.DiffCount == 0)
        {
            MessageBox.Show(this, Strings.DiffNoDifferences, Strings.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        OpenDiffTab(result, $"{name1} \u2194 {name2}");
    }

    // ── Sed transform ──────────────────────────────────────────────

    public void SedTransform()
    {
        var tab = ActiveTab;
        if (tab is null)
        {
            MessageBox.Show(this, Strings.ErrorNoDocumentOpen, Strings.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SedInputDialog();
        dlg.ApplyTheme(ThemeManager.Instance.CurrentTheme);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (!SedCommandParser.TryParse(dlg.Expression, out var cmd))
            return;

        string sourceText = tab.Editor.GetAllText();
        var (resultText, count, ranges) = SedCommandParser.ExecuteWithRanges(cmd, sourceText);

        OpenSedPreviewTab(dlg.Expression, resultText, count,
            _tabs.IndexOf(tab), tab.Editor.Language, ranges);
    }

    private void OpenSedPreviewTab(string expression,
        string transformedText, int replacementCount, int sourceTabIndex,
        string? language, List<(int Start, int Length)> replacementRanges)
    {
        var preview = new SedPreviewControl();
        preview.LoadPreview(expression, transformedText, replacementCount,
            sourceTabIndex, language, replacementRanges);
        preview.SetButtonLabels(Strings.SedPreviewApply, Strings.SedPreviewDiscard,
            Strings.SedReplacementCount, expression, replacementCount);
        preview.ApplyTheme(ThemeManager.Instance.CurrentTheme);
        preview.ApplyRequested += OnSedApply;
        preview.DiscardRequested += OnSedDiscard;

        var dummyEditor = new EditorControl { Visible = false };
        var tab = new TabInfo
        {
            Title = $"sed: {expression}",
            Editor = dummyEditor,
            Tag = preview,
        };
        AddTab(tab);
    }

    private void OnSedApply(SedPreviewControl preview)
    {
        int idx = preview.SourceTabIndex;
        if (idx >= 0 && idx < _tabs.Count)
        {
            ActivateTab(idx);
            ActiveTab!.Editor.TransformDocument(_ => preview.TransformedText);
        }
        CloseSedPreviewTab(preview);
    }

    private void OnSedDiscard(SedPreviewControl preview)
    {
        CloseSedPreviewTab(preview);
    }

    private void CloseSedPreviewTab(SedPreviewControl preview)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Tag == preview)
            {
                CloseTab(i);
                return;
            }
        }
    }

    private bool CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return true;

        TabInfo tab = _tabs[index];

        if (tab.IsModified)
        {
            DialogResult result = MessageBox.Show(
                string.Format(Strings.PromptSaveChanges, tab.Title),
                Strings.AppTitle,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            switch (result)
            {
                case DialogResult.Yes:
                    if (tab.FilePath is null)
                    {
                        ActivateTab(index);
                        SaveAs();
                        if (tab.IsModified) return false; // User cancelled.
                    }
                    else
                    {
                        SaveDocument(tab);
                    }
                    break;

                case DialogResult.Cancel:
                    return false;
            }
        }

        // Cleanup.
        string closedPath = tab.FilePath ?? string.Empty;
        if (tab.FilePath is not null)
            _fileWatcher.Unwatch(tab.FilePath);

        _editorPanel.Controls.Remove(tab.Editor);
        tab.Editor.Dispose();
        if (tab.Tag is DiffViewControl diffView)
        {
            _editorPanel.Controls.Remove(diffView);
            diffView.Dispose();
        }
        else if (tab.Tag is SedPreviewControl sedView)
        {
            sedView.ApplyRequested -= OnSedApply;
            sedView.DiscardRequested -= OnSedDiscard;
            _editorPanel.Controls.Remove(sedView);
            sedView.Dispose();
        }
        _tabs.RemoveAt(index);
        _tabStrip.RemoveTab(index);
        _recoveryManager.RemoveTabRecovery(tab.Id);

        _pluginHost.RaiseDocumentClosed(closedPath);

        // Activate another tab.
        if (_tabs.Count == 0)
        {
            _activeTabIndex = -1;
            UpdateTitleBar();
            _statusBarManager.Clear();
            _menuBuilder.UpdateMenuState(this);
            _symbolListPanel.Clear();
        }
        else
        {
            int newIndex = Math.Min(index, _tabs.Count - 1);
            _activeTabIndex = -1; // Reset so ActivateTab can work.
            ActivateTab(newIndex);
        }

        return true;
    }

    private void SaveDocument(TabInfo tab)
    {
        if (tab.FilePath is null) return;

        if (tab.IsLoading)
        {
            MessageBox.Show(
                Strings.FileStillLoading,
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (tab.Editor.IsMemoryMappedDocument)
        {
            SaveMemoryMappedDocument(tab);
            return;
        }

        try
        {
            _fileWatcher.SuppressNextChange(tab.FilePath);

            Encoding encoding = tab.Editor.EncodingManager?.CurrentEncoding
                ?? new UTF8Encoding(false);
            bool hasBom = tab.Editor.EncodingManager?.HasBom ?? false;
            string le = tab.Editor.LineEnding;

            using var fs = new FileStream(tab.FilePath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 65536);

            if (hasBom)
            {
                byte[] bom = encoding.GetPreamble();
                if (bom.Length > 0)
                    fs.Write(bom, 0, bom.Length);
            }

            WriteDocumentChunked(tab.Editor.Document, fs, encoding, le);

            tab.Editor.FileSizeBytes = fs.Length;
            tab.IsModified = false;
            _recoveryManager.RemoveTabRecovery(tab.Id);
            RefreshTabDisplay(tab);
            UpdateTitleBar();
            UpdateStatusBar();

            _pluginHost.RaiseDocumentSaved(tab.FilePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(Strings.ErrorSavingFile, tab.FilePath, ex.Message),
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Saves an MMF-backed document by writing to a temp file, releasing the
    /// memory-mapped lock, swapping files, and reloading asynchronously.
    /// Shows a progress dialog during the write and reload phases.
    /// </summary>
    private async void SaveMemoryMappedDocument(TabInfo tab)
    {
        if (tab.FilePath is null) return;

        string tmpPath = tab.FilePath + ".saving.tmp";

        // Build a semi-transparent overlay Form positioned over the editor.
        var theme = ThemeManager.Instance.CurrentTheme;
        var (overlayForm, dialogForm, progressLabel, progressBar) = CreateEditorOverlay(tab.Editor, theme);

        try
        {
            Encoding encoding = tab.Editor.EncodingManager?.CurrentEncoding
                ?? new UTF8Encoding(false);
            bool hasBom = tab.Editor.EncodingManager?.HasBom ?? false;
            string le = tab.Editor.LineEnding;

            // Prevent editing during save.
            tab.Editor.IsReadOnly = true;

            // Capture PieceTable reference for background-thread reads.
            PieceTable document = tab.Editor.Document;
            long docLength = document.Length;

            // 1. Write to temp file on background thread with progress.
            var progress = new Progress<long>(written =>
            {
                if (docLength <= 0) return;
                int permille = (int)(written * 1000 / docLength);
                progressBar.Value = Math.Min(permille, 1000);
                string writtenStr = StatusBarManager.FormatFileSize(written);
                string totalStr = StatusBarManager.FormatFileSize(docLength);
                progressLabel.Text = string.Format(Strings.SavingProgressFormat,
                    writtenStr, totalStr);
            });

            await Task.Run(() =>
            {
                using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, bufferSize: 65536);

                if (hasBom)
                {
                    byte[] bom = encoding.GetPreamble();
                    if (bom.Length > 0)
                        fs.Write(bom, 0, bom.Length);
                }

                WriteDocumentChunked(document, fs, encoding, le, progress);
            });

            // Validate: the written file must not be drastically smaller than
            // the original.  A truncated write (e.g. from corrupted piece data)
            // would silently destroy the user's file on the swap below.
            long originalFileSize = new FileInfo(tab.FilePath).Length;
            long writtenSize = new FileInfo(tmpPath).Length;
            if (originalFileSize > 0 && writtenSize < originalFileSize / 2)
            {
                try { File.Delete(tmpPath); } catch { }
                tab.Editor.IsReadOnly = false;
                CloseEditorOverlay(overlayForm, dialogForm);
                MessageBox.Show(
                    $"Save aborted: the written file ({StatusBarManager.FormatFileSize(writtenSize)}) " +
                    $"is much smaller than the original ({StatusBarManager.FormatFileSize(originalFileSize)}). " +
                    $"This likely indicates a bug — your original file has been preserved.",
                    Strings.AppTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // 2. Save editor state before disposing the document.
            long savedScroll = tab.Editor.ScrollMgr.FirstVisibleLine;
            long savedCaret = tab.Editor.CaretOffset;

            // 3. Release the MMF by replacing the document with an empty one.
            SendMessage(tab.Editor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            tab.Editor.Document = new PieceTable(string.Empty);
            SendMessage(tab.Editor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);

            // 4. Now the original file is unlocked — suppress watcher and swap.
            _fileWatcher.SuppressNextChange(tab.FilePath);
            File.Move(tmpPath, tab.FilePath, overwrite: true);

            // 5. Reload asynchronously from the saved file (new MMF).
            long fileSize = new FileInfo(tab.FilePath).Length;
            progressLabel.Text = Strings.ReloadingAfterSave;
            progressBar.Value = 0;
            tab.IsLoading = true; // block recovery timer during reload

            var source = await Task.Run(() =>
                new MemoryMappedFileSource(tab.FilePath, normalizeLineEndings: true, deferScan: true));

            const int FirstBatchChunks = 128;
            const int SubsequentBatchChunks = 2048;
            int batchSize = FirstBatchChunks;
            bool done = false;
            long[]? ownedLineOffsets = null;

            // Keep painting suppressed for the entire reload — the overlay
            // shows progress and prevents interaction.  A single repaint
            // happens after the overlay is removed.
            SendMessage(tab.Editor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

            while (!done)
            {
                done = await Task.Run(() =>
                {
                    bool d = source.ScanNextBatch(batchSize);
                    // Clone line offsets on final batch to avoid shared-array
                    // corruption when edits later modify the cache in-place.
                    if (d && source.LineOffsets is { } lo)
                        ownedLineOffsets = (long[])lo.Clone();
                    return d;
                });

                if (!_tabs.Contains(tab))
                {
                    source.Dispose();
                    SendMessage(tab.Editor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    CloseEditorOverlay(overlayForm, dialogForm);
                    return;
                }

                ITextSource textSource = done
                    ? source
                    : new BorrowedTextSource(source);

                tab.Editor.Document = new PieceTable(textSource);

                // Set owned line offset cache to avoid O(N²) lazy rebuild
                // and shared-array corruption with the MMF source.
                if (done && ownedLineOffsets is not null)
                    tab.Editor.Document.SetLineOffsetCache(ownedLineOffsets);

                tab.Editor.FileSizeBytes = source.ScannedBytes;

                if (fileSize > 0)
                {
                    int permille = (int)(source.ScannedBytes * 1000 / fileSize);
                    progressBar.Value = Math.Min(permille, 1000);
                    string scannedStr = StatusBarManager.FormatFileSize(source.ScannedBytes);
                    string totalStr = StatusBarManager.FormatFileSize(fileSize);
                    progressLabel.Text = string.Format(Strings.ReloadingProgressFormat,
                        scannedStr, totalStr);
                }

                UpdateStatusBar();

                if (!done)
                    batchSize = SubsequentBatchChunks;
            }

            // Resume painting and remove the overlay.
            SendMessage(tab.Editor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            CloseEditorOverlay(overlayForm, dialogForm);

            // 6. Restore state.
            tab.IsLoading = false;
            tab.Editor.FileSizeBytes = fileSize;
            tab.Editor.EncodingManager = new EncodingManager(encoding, hasBom);
            tab.Editor.LineEnding = le;
            tab.Editor.IsMemoryMappedDocument = true;
            tab.Editor.IsReadOnly = false;

            long docLen = tab.Editor.Document.Length;
            if (savedCaret > 0 && savedCaret <= docLen)
                tab.Editor.CaretOffset = savedCaret;
            tab.Editor.ScrollMgr.ScrollToLine(Math.Min(savedScroll, docLen));

            tab.Editor.Invalidate(true);

            tab.IsModified = false;
            _recoveryManager.RemoveTabRecovery(tab.Id);
            RefreshTabDisplay(tab);
            UpdateTitleBar();
            UpdateStatusBar();

            _pluginHost.RaiseDocumentSaved(tab.FilePath);
        }
        catch (Exception ex)
        {
            SendMessage(tab.Editor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            CloseEditorOverlay(overlayForm, dialogForm);

            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { /* best effort */ }
            }

            tab.IsLoading = false;
            tab.Editor.IsReadOnly = false;
            tab.Editor.Invalidate(true);

            MessageBox.Show(
                string.Format(Strings.ErrorSavingFile, tab.FilePath, ex.Message),
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Writes a <see cref="PieceTable"/> to a stream in 1 MB chunks with line
    /// ending conversion.  Safe to call from a background thread when no
    /// concurrent writes are happening to the document.
    /// </summary>
    private static void WriteDocumentChunked(PieceTable document, FileStream fs,
        Encoding encoding, string lineEnding, IProgress<long>? progress = null)
    {
        const int ChunkSize = 1024 * 1024;
        long docLength = document.Length;
        long offset = 0;

        while (offset < docLength)
        {
            long remaining = docLength - offset;
            int take = (int)Math.Min(remaining, ChunkSize);

            string chunk = document.GetText(offset, take);

            if (chunk.Contains('\r'))
                chunk = chunk.Replace("\r\n", "\n").Replace("\r", "\n");

            if (lineEnding == "CRLF")
                chunk = chunk.Replace("\n", "\r\n");
            else if (lineEnding == "CR")
                chunk = chunk.Replace("\n", "\r");

            byte[] bytes = encoding.GetBytes(chunk);
            fs.Write(bytes, 0, bytes.Length);

            offset += take;
            progress?.Report(offset);
        }
    }

    /// <summary>
    /// Creates two Forms over the editor: a semi-transparent overlay for the
    /// dimming effect, and a small opaque dialog with themed progress controls.
    /// The overlay uses <see cref="Form.Opacity"/> so the text behind it stays
    /// visible.  Only this editor is blocked — other tabs remain interactive.
    /// </summary>
    private (Form Overlay, Form Dialog, Label Label, ProgressBar Bar) CreateEditorOverlay(
        EditorControl editor, Bascanka.Editor.Themes.ITheme theme)
    {
        var screenBounds = editor.RectangleToScreen(editor.ClientRectangle);

        // Semi-transparent layer that dims the editor text.
        var overlay = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            BackColor = Color.Black,
            Opacity = 0.35,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = screenBounds.Location,
            ClientSize = screenBounds.Size,
        };

        // Small opaque dialog with progress controls, themed background.
        const int dlgWidth = 440;
        const int dlgHeight = 70;
        int pad = 10;

        // Border color: blend foreground toward background for a subtle edge.
        Color borderColor = Color.FromArgb(
            (theme.EditorForeground.R + theme.EditorBackground.R) / 2,
            (theme.EditorForeground.G + theme.EditorBackground.G) / 2,
            (theme.EditorForeground.B + theme.EditorBackground.B) / 2);

        var dialog = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            ClientSize = new Size(dlgWidth, dlgHeight),
            BackColor = theme.EditorBackground,
        };

        // Draw a 1px border around the dialog.
        dialog.Paint += (_, e) =>
        {
            using var pen = new Pen(borderColor);
            e.Graphics.DrawRectangle(pen, 0, 0,
                dialog.ClientSize.Width - 1, dialog.ClientSize.Height - 1);
        };

        var label = new Label
        {
            Text = string.Format(Strings.SavingProgressFormat, "0 B", "0 B"),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(pad, 4),
            Size = new Size(dlgWidth - pad * 2, 25),
            BackColor = theme.EditorBackground,
            ForeColor = theme.EditorForeground,
        };

        var bar = new ProgressBar
        {
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 1000,
            Location = new Point(pad, 32),
            Size = new Size(dlgWidth - pad * 2, 28),
        };

        dialog.Controls.Add(label);
        dialog.Controls.Add(bar);

        // Center the dialog on the overlay / editor area.
        void centerDialog()
        {
            if (overlay.IsDisposed) return;
            dialog.Location = new Point(
                overlay.Left + (overlay.Width - dlgWidth) / 2,
                overlay.Top + (overlay.Height - dlgHeight) / 2);
        }

        // Follow editor position/size changes.
        void syncPosition(object? s, EventArgs e)
        {
            if (!editor.IsHandleCreated || overlay.IsDisposed) return;
            var bounds = editor.RectangleToScreen(editor.ClientRectangle);
            overlay.Location = bounds.Location;
            overlay.ClientSize = bounds.Size;
            centerDialog();
        }
        editor.SizeChanged += syncPosition;
        editor.LocationChanged += syncPosition;

        // Also follow main form move/resize.
        this.LocationChanged += syncPosition;

        // Hide both forms when this tab is not visible; show when it is.
        editor.VisibleChanged += (_, _) =>
        {
            if (!overlay.IsDisposed) overlay.Visible = editor.Visible;
            if (!dialog.IsDisposed) dialog.Visible = editor.Visible;
        };

        overlay.Show(this);
        centerDialog();
        dialog.Show(this);

        return (overlay, dialog, label, bar);
    }

    /// <summary>
    /// Closes and disposes both overlay and dialog Forms.
    /// </summary>
    private static void CloseEditorOverlay(Form overlay, Form dialog)
    {
        if (!dialog.IsDisposed) { dialog.Close(); dialog.Dispose(); }
        if (!overlay.IsDisposed) { overlay.Close(); overlay.Dispose(); }
    }

    private void WireEditorEvents(EditorControl editor)
    {
        editor.ContextMenuRenderer = t => new ThemedMenuRenderer(t);
        editor.ApplyContextMenuTheme();
        editor.ContentChanged += OnEditorContentChanged;
        editor.CaretPositionChanged += OnEditorCaretPositionChanged;
        editor.SelectionChanged += OnEditorSelectionChanged;
        editor.ZoomChanged += OnEditorZoomChanged;
        editor.HexPanelVisibilityChanged += (_, _) => _menuBuilder.UpdateMenuState(this);
        editor.InsertModeChanged += (_, _) => UpdateStatusBar();
        editor.FindNextRequested += OnEditorFindNextRequested;
        editor.FindAllRequested += OnEditorFindAllRequested;
        editor.FindAllInTabsRequested += OnEditorFindAllInTabsRequested;
        BuildEditorContextMenu(editor);
        ApplyEditorLocalization(editor);
    }

    private static void BuildEditorContextMenu(EditorControl editor)
    {
        var selectedTextMenu = new ToolStripMenuItem(Strings.CtxSelectedText);

        // ── Case conversions ────────────────────────────────────
        var caseMenu = new ToolStripMenuItem(Strings.MenuCaseConversion);
        caseMenu.DropDownItems.AddRange([
            new ToolStripMenuItem(Strings.MenuUpperCase, null, (_, _) => editor.TransformSelection(TextTransformations.ToUpperCase)),
            new ToolStripMenuItem(Strings.MenuLowerCase, null, (_, _) => editor.TransformSelection(TextTransformations.ToLowerCase)),
            new ToolStripMenuItem(Strings.MenuTitleCase, null, (_, _) => editor.TransformSelection(TextTransformations.ToTitleCase)),
            new ToolStripMenuItem(Strings.MenuSwapCase, null, (_, _) => editor.TransformSelection(TextTransformations.SwapCase)),
        ]);

        // ── Encoding ────────────────────────────────────────────
        var encMenu = new ToolStripMenuItem(Strings.MenuTextEncoding);
        encMenu.DropDownItems.AddRange([
            new ToolStripMenuItem(Strings.MenuBase64Encode, null, (_, _) => editor.TransformSelection(TextTransformations.Base64Encode)),
            new ToolStripMenuItem(Strings.MenuBase64Decode, null, (_, _) => editor.TransformSelection(TextTransformations.Base64Decode)),
            new ToolStripSeparator(),
            new ToolStripMenuItem(Strings.MenuUrlEncode, null, (_, _) => editor.TransformSelection(TextTransformations.UrlEncode)),
            new ToolStripMenuItem(Strings.MenuUrlDecode, null, (_, _) => editor.TransformSelection(TextTransformations.UrlDecode)),
            new ToolStripSeparator(),
            new ToolStripMenuItem(Strings.MenuHtmlEncode, null, (_, _) => editor.TransformSelection(TextTransformations.HtmlEncode)),
            new ToolStripMenuItem(Strings.MenuHtmlDecode, null, (_, _) => editor.TransformSelection(TextTransformations.HtmlDecode)),
        ]);

        selectedTextMenu.DropDownItems.AddRange([
            caseMenu, encMenu,
            new ToolStripSeparator(),
            new ToolStripMenuItem(Strings.MenuSortLinesAsc, null, (_, _) => editor.TransformSelection(TextTransformations.SortLinesAscending)),
            new ToolStripMenuItem(Strings.MenuSortLinesDesc, null, (_, _) => editor.TransformSelection(TextTransformations.SortLinesDescending)),
            new ToolStripMenuItem(Strings.MenuRemoveDuplicateLines, null, (_, _) => editor.TransformSelection(TextTransformations.RemoveDuplicateLines)),
            new ToolStripMenuItem(Strings.MenuReverseLines, null, (_, _) => editor.TransformSelection(TextTransformations.ReverseLines)),
            new ToolStripSeparator(),
            new ToolStripMenuItem(Strings.MenuTrimTrailingWhitespace, null, (_, _) => editor.TransformSelection(TextTransformations.TrimTrailingWhitespace)),
            new ToolStripMenuItem(Strings.MenuTrimLeadingWhitespace, null, (_, _) => editor.TransformSelection(TextTransformations.TrimLeadingWhitespace)),
            new ToolStripMenuItem(Strings.MenuCompactWhitespace, null, (_, _) => editor.TransformSelection(TextTransformations.CompactWhitespace)),
            new ToolStripSeparator(),
            new ToolStripMenuItem(Strings.MenuTabsToSpaces, null, (_, _) => editor.TransformSelection(TextTransformations.TabsToSpaces)),
            new ToolStripMenuItem(Strings.MenuSpacesToTabs, null, (_, _) => editor.TransformSelection(TextTransformations.SpacesToTabs)),
            new ToolStripSeparator(),
            new ToolStripMenuItem(Strings.MenuReverseText, null, (_, _) => editor.TransformSelection(TextTransformations.ReverseText)),
        ]);

        // ── JSON (works on selection or entire document) ─────
        var jsonMenu = new ToolStripMenuItem(Strings.MenuJson);
        jsonMenu.DropDownItems.AddRange([
            new ToolStripMenuItem(Strings.MenuJsonFormat, null, (_, _) =>
            {
                if (editor.SelectionLength > 0)
                    editor.TransformSelection(TextTransformations.JsonFormat);
                else
                    editor.TransformDocument(TextTransformations.JsonFormat);
            }),
            new ToolStripMenuItem(Strings.MenuJsonMinimize, null, (_, _) =>
            {
                if (editor.SelectionLength > 0)
                    editor.TransformSelection(TextTransformations.JsonMinimize);
                else
                    editor.TransformDocument(TextTransformations.JsonMinimize);
            }),
        ]);

        editor.AddContextMenuItems([selectedTextMenu, jsonMenu], [selectedTextMenu]);
    }

    private void OnEditorContentChanged(object? sender, EventArgs e)
    {
        if (sender is not EditorControl editor) return;

        TabInfo? tab = _tabs.FirstOrDefault(t => t.Editor == editor);
        if (tab is null) return;

        if (!tab.IsModified)
        {
            tab.IsModified = true;
            RefreshTabDisplay(tab);
            UpdateTitleBar();
        }

        _recoveryManager.MarkDirty(tab.Id);

        // Forward text change events to plugins.
        _pluginHost.RaiseTextChanged(0, 0, 0);
        UpdateStatusBar();
        _menuBuilder.UpdateMenuState(this);
        _symbolListPanel.RequestRefresh();
    }

    private void OnEditorCaretPositionChanged(object? sender, EventArgs e)
    {
        UpdateStatusBar();
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e)
    {
        UpdateStatusBar();
        _menuBuilder.UpdateMenuState(this);
    }

    private void OnEditorZoomChanged(object? sender, EventArgs e)
    {
        UpdateStatusBar();
    }

    private async void OnEditorFindNextRequested(object? sender, FindNextRequestEventArgs e)
    {
        if (sender is not EditorControl editor) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;

        _searchOverlay.CancelRequested = () => cts.Cancel();
        _searchOverlay.ShowOverlay(this);

        var engine = new Core.Search.SearchEngine();
        var document = editor.Document;

        try
        {
            var result = await Task.Run(() =>
            {
                if (cts.Token.IsCancellationRequested) return null;
                return engine.FindNext(document, e.StartOffset, e.Options);
            });

            // Always deliver the result to clear the "Searching..." status,
            // even when cancelled (result will be null in that case).
            editor.SetFindNextResult(result);
        }
        catch (ObjectDisposedException)
        {
            // Buffer disposed during incremental loading.
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Strings.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _searchOverlay.HideOverlay();
        }
    }

    private async void OnEditorFindAllRequested(object? sender, FindAllEventArgs e)
    {
        if (sender is not EditorControl editor) return;

        // Cancel any previous search.
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;

        // Wire Cancel button to the CTS.
        _searchOverlay.CancelRequested = () => cts.Cancel();
        _searchOverlay.ShowOverlay(this);

        var engine = new SearchEngine();
        var document = editor.Document;
        var progress = new Progress<int>(pct => _searchOverlay.UpdateProgress(pct));

        try
        {
            var results = await engine.FindAllAsync(document, e.Options, progress, cts.Token);
            if (cts.Token.IsCancellationRequested)
                return;
            editor.SetFindAllResults(results);
            ShowFindAllResults(e.SearchPattern, results);
        }
        catch (OperationCanceledException)
        {
            // User cancelled — nothing to show.
        }
        catch (ObjectDisposedException)
        {
            // Buffer was disposed during incremental loading — treat as cancel.
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Strings.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _searchOverlay.HideOverlay();
        }
    }

    private async void OnEditorFindAllInTabsRequested(object? sender, FindAllInTabsEventArgs e)
    {
        // Cancel any previous search.
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;

        _searchOverlay.CancelRequested = () => cts.Cancel();
        _searchOverlay.ShowOverlay(this);

        var allResults = new List<SearchResult>();
        var engine = new SearchEngine();
        int tabCount = _tabs.Count;

        try
        {
            for (int i = 0; i < tabCount; i++)
            {
                if (cts.Token.IsCancellationRequested)
                    break;

                var tab = _tabs[i];
                var document = tab.Editor.Document;

                // Progress: each tab contributes an equal share of 0-100%.
                int tabIndex = i;
                var progress = new Progress<int>(pct =>
                {
                    int overall = (tabIndex * 100 + pct) / tabCount;
                    _searchOverlay.UpdateProgress(overall);
                });

                var results = await engine.FindAllAsync(document, e.Options, progress, cts.Token);
                if (cts.Token.IsCancellationRequested)
                    break;

                foreach (var r in results)
                {
                    allResults.Add(new SearchResult
                    {
                        Offset = r.Offset,
                        Length = r.Length,
                        LineNumber = r.LineNumber,
                        ColumnNumber = r.ColumnNumber,
                        LineText = r.LineText,
                        FilePath = tab.FilePath ?? tab.Title,
                    });
                }
            }

            _findResultsPanel.Theme = ThemeManager.Instance.CurrentTheme;
            _findResultsPanel.AddSearchResults(allResults, e.Options.Pattern,
                Strings.ScopeAllOpenTabs, multiFile: true);
            EnsureFindResultsTab();
            _bottomTabStrip.SelectTab("findResults");
            ShowSelectedBottomTab();
            IsBottomPanelVisible = true;
            _menuBuilder.UpdateMenuState(this);
        }
        catch (OperationCanceledException)
        {
            // User cancelled — nothing to show.
        }
        catch (ObjectDisposedException)
        {
            // Buffer was disposed during incremental loading — treat as cancel.
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Strings.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _searchOverlay.HideOverlay();
        }
    }

    private void OnTabSelected(object? sender, TabEventArgs e)
    {
        ActivateTab(e.Index);
    }

    private void OnTabsReordered(object? sender, EventArgs e)
    {
        // Sync MainForm's _tabs list to match TabStrip's reordered list.
        _tabs.Clear();
        _tabs.AddRange(_tabStrip.Tabs);

        // Update active index to point to the same TabInfo in the new order.
        _activeTabIndex = _tabStrip.SelectedIndex;
    }

    private void OnTabCloseRequested(object? sender, TabEventArgs e)
    {
        CloseTab(e.Index);
    }

    private void UpdateTitleBar()
    {
        TabInfo? tab = ActiveTab;
        if (tab is null)
        {
            Text = Strings.AppTitle;
            return;
        }

        string fileDisplay = tab.FilePath ?? tab.Title;
        string modified = tab.IsModified ? "* " : "";
        Text = $"{modified}{fileDisplay} - {Strings.AppTitle}";
    }

    private void RefreshSymbolList(TabInfo tab)
    {
        string langId = tab.Editor.CurrentLexer?.LanguageId ?? "plaintext";
        _symbolListPanel.Attach(tab.Editor.Document, langId);
        _symbolListPanel.RefreshNow();
    }

    private void RefreshTabDisplay(TabInfo tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index >= 0)
            _tabStrip.UpdateTab(index);
    }

    private void ApplyTheme(ITheme theme)
    {
        // ── Form ──────────────────────────────────────────────────────
        BackColor = theme.EditorBackground;
        ForeColor = theme.EditorForeground;

        // ── Menu strip with custom renderer ───────────────────────────
        _menuStrip.BackColor = theme.MenuBackground;
        _menuStrip.ForeColor = theme.MenuForeground;
        _menuStrip.Renderer = new ThemedMenuRenderer(theme);

        // ── Status bar ────────────────────────────────────────────────
        _statusStrip.BackColor = theme.StatusBarBackground;
        _statusStrip.ForeColor = theme.StatusBarForeground;
        foreach (ToolStripItem item in _statusStrip.Items)
        {
            item.BackColor = theme.StatusBarBackground;
            item.ForeColor = theme.StatusBarForeground;
        }

        // ── Tab strip ─────────────────────────────────────────────────
        _tabStrip.Theme = theme;
        _tabStrip.Invalidate();

        // ── Panels and split containers ───────────────────────────────
        _editorPanel.BackColor = theme.EditorBackground;
        _sidePanel.BackColor = theme.EditorBackground;
        _bottomPanel.BackColor = theme.EditorBackground;
        _horizontalSplit.BackColor = theme.EditorBackground;
        _verticalSplit.BackColor = theme.EditorBackground;

        // ── All open editor controls ──────────────────────────────────
        foreach (var tab in _tabs)
        {
            tab.Editor.Theme = theme;
            if (tab.Tag is DiffViewControl diffView)
                diffView.ApplyTheme(theme);
            else if (tab.Tag is SedPreviewControl sedView)
                sedView.ApplyTheme(theme);
        }

        // ── Find results panel ────────────────────────────────────────
        _findResultsPanel.Theme = theme;

        // ── Bottom panel tab strip and terminal ─────────────────────
        _bottomTabStrip.Theme = theme;
        _terminalPanel?.Theme = theme;

        // ── Symbol list panel ────────────────────────────────────────
        _symbolListPanel.Theme = theme;

        Invalidate(true);
    }

    private void RegisterDefaultShortcuts()
    {
        _shortcutManager.RegisterShortcut("New", Keys.N, ctrl: true, shift: false, alt: false, NewDocument);
        _shortcutManager.RegisterShortcut("Open", Keys.O, ctrl: true, shift: false, alt: false, () => OpenFile());
        _shortcutManager.RegisterShortcut("Save", Keys.S, ctrl: true, shift: false, alt: false, SaveCurrentDocument);
        _shortcutManager.RegisterShortcut("SaveAs", Keys.S, ctrl: true, shift: true, alt: false, SaveAs);
        _shortcutManager.RegisterShortcut("CloseTab", Keys.W, ctrl: true, shift: false, alt: false, () => CloseCurrentDocument());
        _shortcutManager.RegisterShortcut("NextTab", Keys.Tab, ctrl: true, shift: false, alt: false, NextTab);
        _shortcutManager.RegisterShortcut("PrevTab", Keys.Tab, ctrl: true, shift: true, alt: false, PreviousTab);
        _shortcutManager.RegisterShortcut("Find", Keys.F, ctrl: true, shift: false, alt: false, ShowFind);
        _shortcutManager.RegisterShortcut("Replace", Keys.H, ctrl: true, shift: false, alt: false, ShowFindReplace);
        _shortcutManager.RegisterShortcut("GoToLine", Keys.G, ctrl: true, shift: false, alt: false, ShowGoToLine);
        _shortcutManager.RegisterShortcut("PlayMacro", Keys.F5, ctrl: false, shift: false, alt: false, PlayMacro);
        _shortcutManager.RegisterShortcut("FullScreen", Keys.F11, ctrl: false, shift: false, alt: false, ToggleFullScreen);
        _shortcutManager.RegisterShortcut("CommandPalette", Keys.P, ctrl: true, shift: true, alt: false, ShowCommandPalette);
        _shortcutManager.RegisterShortcut("Terminal", Keys.Oemtilde, ctrl: true, shift: false, alt: false, ToggleTerminal);
    }

    private void ApplyLocalizedMenuTexts()
    {
        _tabStrip.SetMenuTexts(
            Strings.TabMenuClose,
            Strings.TabMenuCloseOthers,
            Strings.TabMenuCloseAll,
            Strings.TabMenuCloseToRight,
            Strings.TabMenuCopyPath,
            Strings.TabMenuOpenInExplorer);

        _findResultsPanel.SetMenuTexts(
            Strings.FindResultsCopyLine,
            Strings.FindResultsCopyAll,
            Strings.FindResultsCopyPath,
            Strings.FindResultsOpenInNewTab,
            Strings.FindResultsRemoveSearch,
            Strings.FindResultsClearAll,
            Strings.FindResultsHeader,
            Strings.FindResultsHeaderFormat,
            Strings.FindResultMatchFormat,
            Strings.FindResultMatchFilesFormat);

        // Update bottom panel tab titles.
        _bottomTabStrip.SetTabTitle("findResults", Strings.FindResultsHeader);
        if (_bottomTabStrip.HasTab("terminal"))
            _bottomTabStrip.SetTabTitle("terminal", Strings.MenuTerminal.Replace("&", ""));

        // Update all open editor context menus.
        foreach (var tab in _tabs)
            ApplyEditorLocalization(tab.Editor);
    }

    private static void ApplyEditorLocalization(EditorControl editor)
    {
        editor.SetContextMenuTexts(
            Strings.CtxUndo, Strings.CtxRedo,
            Strings.CtxCut, Strings.CtxCopy, Strings.CtxPaste,
            Strings.CtxDelete, Strings.CtxSelectAll);

        editor.FindPanel?.SetButtonTexts(
            Strings.FindPanelMarkAll,
            Strings.FindPanelFindAll,
            Strings.FindPanelFindInTabs,
            Strings.FindPanelReplace,
            Strings.FindPanelReplaceAll);
    }

    /// <summary>
    /// Resolves how a binary file should be opened: "hex", "text", or null (cancelled).
    /// Checks saved per-extension preferences first; if none, shows the dialog.
    /// </summary>
    private string? ResolveBinaryOpenMode(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();

        // Check saved preference.
        string? saved = SettingsManager.GetBinaryExtPref(ext);
        if (saved is not null)
            return saved;

        // Show dialog.
        string fileName = Path.GetFileName(path);
        using var dlg = new BinaryFileOpenDialog(fileName, ext);
        dlg.ApplyTheme(ThemeManager.Instance.CurrentTheme);

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return null;

        string mode = dlg.IsHexMode ? "hex" : "text";

        if (dlg.RememberChoice && !string.IsNullOrEmpty(ext))
            SettingsManager.SetBinaryExtPref(ext, mode);

        return mode;
    }

    /// <summary>
    /// Opens a binary file in hex-only mode (no text editor).
    /// </summary>
    private void OpenBinaryFile(string path, byte[] rawBytes)
    {
        var pieceTable = new PieceTable(string.Empty);
        var editor = new EditorControl(pieceTable)
        {
            Theme = ThemeManager.Instance.CurrentTheme,
            IsReadOnly = true
        };
        WireEditorEvents(editor);
        editor.ShowHexOnly(rawBytes);

        var tab = new TabInfo
        {
            Title = Path.GetFileName(path),
            FilePath = path,
            IsModified = false,
            IsBinaryMode = true,
            Editor = editor,
        };

        AddTab(tab);
        _recentFilesManager.AddFile(path);
        _menuBuilder.RefreshRecentFilesMenu(this);
        _fileWatcher.Watch(tab);
        _pluginHost.RaiseDocumentOpened(path);
    }

    /// <summary>
    /// Normalizes line endings to <c>\n</c> in a single pass while simultaneously
    /// detecting the dominant line ending style. Avoids intermediate string copies
    /// that <c>string.Replace</c> chains would create.
    /// </summary>
    private static (string Normalized, string LineEnding) NormalizeLineEndings(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        bool needsNormalization = false;

        // First pass: count line endings and check if normalization is needed.
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                needsNormalization = true;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++; // skip the \n
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        string lineEnding;
        if (crlf >= lf && crlf >= cr) lineEnding = "CRLF";
        else if (lf >= cr) lineEnding = "LF";
        else lineEnding = "CR";

        // If no \r found, text is already normalized (only \n or no line endings).
        if (!needsNormalization)
            return (text, lineEnding);

        // Single-pass normalization: replace \r\n and \r with \n.
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                sb.Append('\n');
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++; // skip the \n in \r\n
            }
            else
            {
                sb.Append(text[i]);
            }
        }

        return (sb.ToString(), lineEnding);
    }

    /// <summary>
    /// Files larger than this threshold use memory-mapped loading
    /// on a background thread. Configurable via Settings.
    /// </summary>
    private static long LargeFileThreshold { get; set; } = 10L * 1024 * 1024;
    private static long WordWrapMaxFileSizeBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>
    /// Auto-save interval for crash recovery, in seconds. Configurable via Settings.
    /// </summary>
    internal static int ConfigAutoSaveIntervalSeconds { get; set; } = RecoveryManager.DefaultIntervalSeconds;

    /// <summary>
    /// Detects whether a file is binary by checking for null bytes
    /// in the first 8 KB of the content. Files with a UTF-16/UTF-32
    /// BOM are not considered binary.
    /// </summary>
    private static bool IsBinaryFile(byte[] data)
    {
        if (data.Length < 2)
            return false;

        // UTF-16 LE/BE BOM — not binary.
        if ((data[0] == 0xFF && data[1] == 0xFE) ||
            (data[0] == 0xFE && data[1] == 0xFF))
            return false;

        // UTF-32 BE BOM.
        if (data.Length >= 4 &&
            data[0] == 0x00 && data[1] == 0x00 &&
            data[2] == 0xFE && data[3] == 0xFF)
            return false;

        int checkLength = Math.Min(data.Length, 8192);
        for (int i = 0; i < checkLength; i++)
        {
            if (data[i] == 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detects whether a file is binary by reading only the first 8 KB
    /// via a small <see cref="FileStream"/> instead of loading the entire file.
    /// Files with a UTF-16/UTF-32 BOM are not considered binary even though
    /// they naturally contain null bytes.
    /// Recognises known binary file signatures (PDF, ZIP, PE, etc.) in
    /// addition to the null-byte heuristic.
    /// </summary>
    private static bool IsBinaryFileFromStream(string path)
    {
        byte[] buffer = new byte[8192];
        int bytesRead;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bytesRead = fs.Read(buffer, 0, buffer.Length);
        }

        if (bytesRead < 2)
            return false;

        // UTF-16 LE BOM (FF FE) or UTF-16 BE BOM (FE FF) — not binary.
        if ((buffer[0] == 0xFF && buffer[1] == 0xFE) ||
            (buffer[0] == 0xFE && buffer[1] == 0xFF))
            return false;

        // UTF-32 BE BOM (00 00 FE FF) — not binary.
        if (bytesRead >= 4 &&
            buffer[0] == 0x00 && buffer[1] == 0x00 &&
            buffer[2] == 0xFE && buffer[3] == 0xFF)
            return false;

        // Known binary file signatures (magic bytes).
        if (HasBinarySignature(buffer.AsSpan(0, bytesRead)))
            return true;

        for (int i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks for well-known binary file magic bytes at the start of the buffer.
    /// </summary>
    private static bool HasBinarySignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return false;

        // PDF: %PDF
        if (data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46)
            return true;

        // PE executable: MZ
        if (data[0] == 0x4D && data[1] == 0x5A)
            return true;

        // ZIP / DOCX / XLSX / JAR / APK: PK\x03\x04
        if (data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04)
            return true;

        // GZip: \x1F\x8B
        if (data[0] == 0x1F && data[1] == 0x8B)
            return true;

        // PNG: \x89PNG
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return true;

        // JPEG: \xFF\xD8\xFF
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return true;

        // GIF: GIF8
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
            return true;

        // BMP: BM
        if (data[0] == 0x42 && data[1] == 0x4D)
            return true;

        // TIFF: II\x2A\x00 or MM\x00\x2A
        if ((data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00) ||
            (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A))
            return true;

        // WebP: RIFF....WEBP
        if (data.Length >= 12 &&
            data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            return true;

        // 7-Zip: 7z\xBC\xAF\x27\x1C
        if (data.Length >= 6 &&
            data[0] == 0x37 && data[1] == 0x7A && data[2] == 0xBC && data[3] == 0xAF &&
            data[4] == 0x27 && data[5] == 0x1C)
            return true;

        // RAR: Rar!
        if (data[0] == 0x52 && data[1] == 0x61 && data[2] == 0x72 && data[3] == 0x21)
            return true;

        // ELF: \x7FELF
        if (data[0] == 0x7F && data[1] == 0x45 && data[2] == 0x4C && data[3] == 0x46)
            return true;

        // Mach-O: \xFE\xED\xFA\xCE / \xFE\xED\xFA\xCF / \xCE\xFA\xED\xFE / \xCF\xFA\xED\xFE
        if ((data[0] == 0xFE && data[1] == 0xED && data[2] == 0xFA && (data[3] == 0xCE || data[3] == 0xCF)) ||
            ((data[0] == 0xCE || data[0] == 0xCF) && data[1] == 0xFA && data[2] == 0xED && data[3] == 0xFE))
            return true;

        // WASM: \x00asm
        if (data[0] == 0x00 && data[1] == 0x61 && data[2] == 0x73 && data[3] == 0x6D)
            return true;

        return false;
    }

    /// <summary>
    /// Opens a large file with incremental progressive loading.  The user sees
    /// content appear and grow as chunks are scanned in the background.
    /// </summary>
    private async void OpenLargeFile(string path, long fileSize,
        System.Text.Encoding? forcedEncoding = null)
    {
        // 1. Create tab immediately with an empty document.
        var editor = new EditorControl(new PieceTable(string.Empty))
        {
            FileSizeBytes = fileSize,
            Theme = ThemeManager.Instance.CurrentTheme,
            IsReadOnly = true
        };
        WireEditorEvents(editor);

        string ext = Path.GetExtension(path);
        ILexer? lexer = LexerRegistry.Instance.GetLexerByExtension(ext);
        if (lexer is not null)
            editor.SetLexer(lexer);

        var tab = new TabInfo
        {
            Title = Path.GetFileName(path),
            FilePath = path,
            IsModified = false,
            Editor = editor,
        };

        AddTab(tab);
        _recentFilesManager.AddFile(path);
        _menuBuilder.RefreshRecentFilesMenu(this);
        _fileWatcher.Watch(tab);
        tab.IsLoading = true;

        try
        {
            // 2. Create MMF source with deferred scanning.
            var source = await Task.Run(() =>
                new MemoryMappedFileSource(path, encoding: forcedEncoding,
                    normalizeLineEndings: true, deferScan: true));

            // 3. Incremental scanning loop — scan batches and swap the document
            //    so the user can scroll further with each pass.
            const int FirstBatchChunks = 128;       // ~8 MB — quick first content
            const int SubsequentBatchChunks = 2048;  // ~128 MB per subsequent batch

            int batchSize = FirstBatchChunks;
            bool done = false;

            while (!done)
            {
                // Clone line offsets on the background thread for the final batch
                // so the PieceTable owns its cache.  Without a separate copy,
                // in-place shifts in UpdateLineOffsetCache corrupt the
                // MemoryMappedFileSource's immutable _lineOffsets array.
                long[]? ownedLineOffsets = null;
                done = await Task.Run(() =>
                {
                    bool d = source.ScanNextBatch(batchSize);
                    if (d && source.LineOffsets is { } lo)
                        ownedLineOffsets = (long[])lo.Clone();
                    return d;
                });

                // Stop if tab was closed while scanning.
                if (!_tabs.Contains(tab))
                {
                    source.Dispose();
                    return;
                }

                long savedScroll = tab.Editor.ScrollMgr.FirstVisibleLine;
                long savedCaret = tab.Editor.CaretOffset;
                var selMgr = tab.Editor.SelectionMgr;
                bool hadSelection = selMgr.HasSelection;
                long savedSelStart = selMgr.SelectionStart;
                long savedSelEnd = selMgr.SelectionEnd;

                ITextSource textSource = done
                    ? source
                    : new BorrowedTextSource(source);

                // Suppress painting during document swap to avoid blinking.
                SendMessage(tab.Editor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                try
                {
                    tab.Editor.Document = new PieceTable(textSource);

                    // Replace the shared cache with the owned clone so edits
                    // don't corrupt the source's line offsets.
                    if (ownedLineOffsets is not null)
                        tab.Editor.Document.SetLineOffsetCache(ownedLineOffsets);

                    // Restore caret BEFORE scroll so that the scroll position
                    // the user is actually looking at wins over EnsureVisible.
                    long docLen = tab.Editor.Document.Length;
                    if (hadSelection && savedSelStart < docLen)
                    {
                        long clampedEnd = Math.Min(savedSelEnd, docLen);
                        tab.Editor.Select(savedSelStart, (int)(clampedEnd - savedSelStart));
                    }
                    else if (savedCaret > 0 && savedCaret <= docLen)
                    {
                        tab.Editor.CaretOffset = savedCaret;
                    }

                    tab.Editor.ScrollMgr.ScrollToLine(savedScroll);
                }
                finally
                {
                    SendMessage(tab.Editor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    tab.Editor.Invalidate(true);
                }

                tab.Editor.FileSizeBytes = source.ScannedBytes;
                if (ActiveTab == tab)
                    _statusBarManager.ShowLoadingProgress(source.ScannedBytes, fileSize);

                if (!done)
                    batchSize = SubsequentBatchChunks;
            }

            // Final setup — set exact file size from disk.
            tab.Editor.FileSizeBytes = fileSize;
            tab.Editor.EncodingManager = new EncodingManager(source.Encoding,
                source.Encoding.GetPreamble().Length > 0);
            tab.Editor.LineEnding = source.DetectedLineEnding;
            tab.Editor.IsMemoryMappedDocument = true;
            tab.Editor.IsReadOnly = false;
            tab.IsLoading = false;

            if (lexer is not null)
                tab.Editor.SetLexer(lexer);

            if (ActiveTab == tab)
                UpdateStatusBar();
            _menuBuilder.RefreshEncodingMenu(this);
            _pluginHost.RaiseDocumentOpened(path);
        }
        catch (UnauthorizedAccessException)
        {
            // Remove the placeholder tab that was already added.
            int idx = _tabs.IndexOf(tab);
            if (idx >= 0)
            {
                _tabs.RemoveAt(idx);
                _tabStrip.RemoveTab(idx);
            }
            OfferAdminElevation(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(Strings.ErrorOpeningFile, path, ex.Message),
                Strings.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            tab.Editor.IsReadOnly = false;
        }
    }

    private static string BuildFileFilter()
    {
        return string.Join("|",
            Strings.FilterAllFiles + " (*.*)|*.*",
            "C# (*.cs)|*.cs",
            "JavaScript (*.js)|*.js",
            "TypeScript (*.ts;*.tsx)|*.ts;*.tsx",
            "Python (*.py)|*.py",
            "HTML (*.html;*.htm)|*.html;*.htm",
            "CSS (*.css)|*.css",
            "XML (*.xml)|*.xml",
            "JSON (*.json)|*.json",
            "SQL (*.sql)|*.sql",
            "Markdown (*.md)|*.md",
            "Text (*.txt)|*.txt");
    }

    /// <summary>
    /// Returns true when the Win32 keyboard focus belongs to a TextBox or
    /// ComboBox (including the internal native EDIT child of a ComboBox).
    /// </summary>
    private static bool IsTextInputFocused()
    {
        IntPtr hwnd = GetFocus();
        if (hwnd == IntPtr.Zero) return false;

        // Try to resolve a managed Control from the focused HWND.
        Control? c = Control.FromHandle(hwnd);
        if (c is TextBox or ComboBox) return true;

        // ComboBox DropDown style: the internal EDIT child has focus.
        // FromHandle returns null for the native EDIT, so walk native
        // parent HWNDs until we find a managed ComboBox or TextBox.
        if (c == null)
        {
            IntPtr parent = GetParent(hwnd);
            while (parent != IntPtr.Zero)
            {
                Control? parentCtrl = Control.FromHandle(parent);
                if (parentCtrl is TextBox or ComboBox) return true;
                if (parentCtrl != null) break; // reached a managed control that isn't a text input
                parent = GetParent(parent);
            }
        }

        return false;
    }
}
