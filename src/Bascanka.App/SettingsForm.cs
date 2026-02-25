using System.Text.Json;
using Bascanka.Editor.Controls;
using Bascanka.Editor.Panels;
using Bascanka.Editor.Tabs;
using Bascanka.Editor.Themes;

namespace Bascanka.App;

/// <summary>
/// Settings dialog with grouped categories on the left and settings on the right.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly ITheme _theme;
    private readonly ListBox _categoryList;
    private readonly Panel _contentPanel;
    private readonly Panel[] _categoryPanels;

    // ── Editor controls ─────────────────────────────────────────────
    private ComboBox _fontFamilyCombo = null!;
    private NumericUpDown _fontSizeNum = null!;
    private NumericUpDown _tabWidthNum = null!;
    private NumericUpDown _scrollSpeedNum = null!;
    private CheckBox _autoIndentCheck = null!;
    private NumericUpDown _caretScrollBufferNum = null!;

    // ── Appearance controls ──────────────────────────────────────────
    private ComboBox _themeCombo = null!;
    private ComboBox _uiLanguageCombo = null!;
    private CheckBox _recentFilesSeparatedCheck = null!;
    private Panel _colorGridPanel = null!;
    private readonly Dictionary<string, Color> _pendingOverrides = new(StringComparer.OrdinalIgnoreCase);

    // ── Display controls ────────────────────────────────────────────
    private NumericUpDown _caretBlinkNum = null!;
    private NumericUpDown _maxTabWidthNum = null!;
    private NumericUpDown _textLeftPaddingNum = null!;
    private NumericUpDown _lineSpacingNum = null!;
    private NumericUpDown _minZoomFontNum = null!;
    private NumericUpDown _whitespaceOpacityNum = null!;
    private NumericUpDown _foldIndicatorOpacityNum = null!;
    private NumericUpDown _gutterPaddingLeftNum = null!;
    private NumericUpDown _gutterPaddingRightNum = null!;
    private NumericUpDown _foldButtonSizeNum = null!;
    private NumericUpDown _bookmarkSizeNum = null!;
    private NumericUpDown _tabHeightNum = null!;
    private NumericUpDown _minTabWidthNum = null!;
    private NumericUpDown _menuItemPaddingNum = null!;
    private NumericUpDown _terminalPaddingNum = null!;

    // ── Performance controls ────────────────────────────────────────
    private NumericUpDown _largeFileNum = null!;
    private NumericUpDown _foldingMaxNum = null!;
    private NumericUpDown _wordWrapMaxNum = null!;
    private NumericUpDown _maxRecentFilesNum = null!;
    private NumericUpDown _searchHistoryNum = null!;
    private NumericUpDown _searchDebounceNum = null!;
    private NumericUpDown _autoSaveIntervalNum = null!;

    // ── System controls ─────────────────────────────────────────────
    private CheckBox _contextMenuCheckBox = null!;
    private CheckBox _newExplorerContextMenuCheckBox = null!;

    // ── Files controls ───────────────────────────────────────────────
    private ListView _binaryExtPrefsList = null!;

    // ── Color grid entries ───────────────────────────────────────────
    private static readonly (string Group, string Property, string DisplayName)[] ColorEntries =
    [
        ("Editor", "EditorBackground", "Background"),
        ("Editor", "EditorForeground", "Foreground"),
        ("Editor", "LineHighlight", "Line Highlight"),
        ("Editor", "SelectionBackground", "Selection Background"),
        ("Editor", "SelectionForeground", "Selection Foreground"),
        ("Editor", "CaretColor", "Caret"),
        ("Editor", "BracketMatchBackground", "Bracket Match"),
        ("Editor", "MatchHighlight", "Match Highlight"),
        ("Gutter", "GutterBackground", "Background"),
        ("Gutter", "GutterForeground", "Foreground"),
        ("Gutter", "GutterCurrentLine", "Current Line"),
        ("Gutter", "FoldingMarker", "Folding Marker"),
        ("Tabs", "TabBarBackground", "Bar Background"),
        ("Tabs", "TabActiveBackground", "Active Background"),
        ("Tabs", "TabInactiveBackground", "Inactive Background"),
        ("Tabs", "TabActiveForeground", "Active Foreground"),
        ("Tabs", "TabInactiveForeground", "Inactive Foreground"),
        ("Tabs", "TabBorder", "Border"),
        ("StatusBar", "StatusBarBackground", "Background"),
        ("StatusBar", "StatusBarForeground", "Foreground"),
        ("FindPanel", "FindPanelBackground", "Background"),
        ("FindPanel", "FindPanelForeground", "Foreground"),
        ("Menus", "MenuBackground", "Background"),
        ("Menus", "MenuForeground", "Foreground"),
        ("Menus", "MenuHighlight", "Highlight"),
        ("ScrollBar", "ScrollBarBackground", "Background"),
        ("ScrollBar", "ScrollBarThumb", "Thumb"),
        ("Diff", "DiffAddedBackground", "Added Background"),
        ("Diff", "DiffRemovedBackground", "Removed Background"),
        ("Diff", "DiffModifiedBackground", "Modified Background"),
        ("Diff", "DiffModifiedCharBackground", "Modified Char Background"),
        ("Diff", "DiffPaddingBackground", "Padding Background"),
        ("Diff", "DiffGutterMarker", "Gutter Marker"),
        ("Other", "ModifiedIndicator", "Modified Indicator"),
    ];

    public SettingsForm()
    {
        _theme = ThemeManager.Instance.CurrentTheme;

        Text = Strings.SettingsTitle;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(640, 480);

        // Restore saved size or use a larger default.
        int savedW = SettingsManager.GetInt(SettingsManager.KeySettingsWidth, 860);
        int savedH = SettingsManager.GetInt(SettingsManager.KeySettingsHeight, 640);
        ClientSize = new Size(Math.Max(savedW, MinimumSize.Width),
                              Math.Max(savedH, MinimumSize.Height));

        BackColor = _theme.EditorBackground;
        ForeColor = _theme.EditorForeground;

        // Persist size on close.
        FormClosing += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal)
            {
                SettingsManager.SetInt(SettingsManager.KeySettingsWidth, ClientSize.Width);
                SettingsManager.SetInt(SettingsManager.KeySettingsHeight, ClientSize.Height);
            }
        };

        // ── Category list (left) ────────────────────────────────────
        _categoryList = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 160,
            Font = new Font("Segoe UI", 11f),
            BackColor = _theme.GutterBackground,
            ForeColor = _theme.EditorForeground,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
        };
        _categoryList.Items.Add(Strings.SettingsCategoryEditor);
        _categoryList.Items.Add(Strings.SettingsCategoryAppearance);
        _categoryList.Items.Add(Strings.SettingsCategoryDisplay);
        _categoryList.Items.Add(Strings.SettingsCategoryPerformance);
        _categoryList.Items.Add(Strings.SettingsCategoryFiles);
        _categoryList.Items.Add(Strings.SettingsCategorySystem);
        _categoryList.SelectedIndexChanged += (_, _) => ShowCategory(_categoryList.SelectedIndex);

        // ── Content panel (right) ───────────────────────────────────
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 0),
        };

        // ── Build category panels ───────────────────────────────────
        var editorPanel = BuildEditorPanel();
        var appearancePanel = BuildAppearancePanel();
        var displayPanel = BuildDisplayPanel();
        var perfPanel = BuildPerformancePanel();
        var filesPanel = BuildFilesPanel();
        var systemPanel = BuildSystemPanel();
        _categoryPanels = [editorPanel, appearancePanel, displayPanel, perfPanel, filesPanel, systemPanel];

        foreach (var p in _categoryPanels)
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            _contentPanel.Controls.Add(p);
        }

        // ── Bottom buttons ──────────────────────────────────────────
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };

        var resetButton = CreateButton(Strings.SettingsResetDefaults);
        resetButton.Width = 160;
        resetButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        resetButton.Location = new Point(12, 10);
        resetButton.Click += OnResetClick;

        var okButton = CreateButton(Strings.ButtonOK);
        okButton.DialogResult = DialogResult.OK;
        okButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        okButton.Location = new Point(bottomPanel.Width - 200, 10);
        okButton.Click += OnOkClick;

        var cancelButton = CreateButton(Strings.ButtonCancel);
        cancelButton.DialogResult = DialogResult.Cancel;
        cancelButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        cancelButton.Location = new Point(bottomPanel.Width - 100, 10);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        bottomPanel.Controls.AddRange([resetButton, okButton, cancelButton]);

        // ── Separator between list and content ──────────────────────
        var separator = new Panel
        {
            Dock = DockStyle.Left,
            Width = 1,
            BackColor = _theme.TabBorder,
        };

        // ── Assemble ────────────────────────────────────────────────
        Controls.Add(_contentPanel);
        Controls.Add(separator);
        Controls.Add(_categoryList);
        Controls.Add(bottomPanel);

        // Select first category.
        _categoryList.SelectedIndex = 0;

        // Suppress warnings for fields assigned in Build* methods.
        _ = _fontFamilyCombo; _ = _fontSizeNum; _ = _tabWidthNum;
        _ = _scrollSpeedNum; _ = _autoIndentCheck; _ = _caretScrollBufferNum;
        _ = _themeCombo; _ = _uiLanguageCombo; _ = _recentFilesSeparatedCheck; _ = _colorGridPanel;
        _ = _caretBlinkNum; _ = _maxTabWidthNum;
        _ = _textLeftPaddingNum; _ = _lineSpacingNum; _ = _minZoomFontNum;
        _ = _whitespaceOpacityNum; _ = _foldIndicatorOpacityNum;
        _ = _gutterPaddingLeftNum; _ = _gutterPaddingRightNum;
        _ = _foldButtonSizeNum; _ = _bookmarkSizeNum;
        _ = _tabHeightNum; _ = _minTabWidthNum; _ = _menuItemPaddingNum; _ = _terminalPaddingNum;
        _ = _largeFileNum; _ = _foldingMaxNum; _ = _wordWrapMaxNum; _ = _maxRecentFilesNum;
        _ = _searchHistoryNum; _ = _searchDebounceNum;
        _ = _contextMenuCheckBox; _ = _newExplorerContextMenuCheckBox;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Build category panels
    // ────────────────────────────────────────────────────────────────────

    private Panel BuildEditorPanel()
    {
        var panel = new Panel { AutoScroll = true };
        int y = 0;

        // Font Family
        _fontFamilyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
            Font = ControlFont(),
            BackColor = _theme.FindPanelBackground,
            ForeColor = _theme.EditorForeground,
        };
        PopulateMonospaceFonts(_fontFamilyCombo);
        string currentFont = SettingsManager.GetString(SettingsManager.KeyFontFamily, EditorControl.DefaultFontFamily);
        int idx = _fontFamilyCombo.Items.IndexOf(currentFont);
        _fontFamilyCombo.SelectedIndex = idx >= 0 ? idx : _fontFamilyCombo.Items.IndexOf("Consolas");
        if (_fontFamilyCombo.SelectedIndex < 0 && _fontFamilyCombo.Items.Count > 0) _fontFamilyCombo.SelectedIndex = 0;
        y = AddLabeledControl(panel, Strings.SettingsFontFamily, _fontFamilyCombo, null, y, Strings.SettingsFontFamilyDesc);

        // Font Size
        _fontSizeNum = CreateNumeric(6, 72, 1, SettingsManager.GetInt(SettingsManager.KeyFontSize, (int)EditorControl.DefaultFontSize));
        y = AddLabeledControl(panel, Strings.SettingsFontSize, _fontSizeNum, null, y, Strings.SettingsFontSizeDesc);

        // Tab Width
        _tabWidthNum = CreateNumeric(1, 16, 1, SettingsManager.GetInt(SettingsManager.KeyTabWidth, EditorControl.DefaultTabWidth));
        y = AddLabeledControl(panel, Strings.SettingsTabWidth, _tabWidthNum, null, y, Strings.SettingsTabWidthDesc);

        // Auto Indent
        _autoIndentCheck = CreateCheckBox(SettingsManager.GetBool(SettingsManager.KeyAutoIndent, true));
        y = AddLabeledControl(panel, Strings.SettingsAutoIndent, _autoIndentCheck, null, y, Strings.SettingsAutoIndentDesc);

        // Scroll Speed
        _scrollSpeedNum = CreateNumeric(1, 20, 1, SettingsManager.GetInt(SettingsManager.KeyScrollSpeed, EditorControl.DefaultScrollSpeed));
        y = AddLabeledControl(panel, Strings.SettingsScrollSpeed, _scrollSpeedNum, Strings.SettingsScrollSpeedUnit, y, Strings.SettingsScrollSpeedDesc);

        // Caret Scroll Buffer
        _caretScrollBufferNum = CreateNumeric(0, 20, 1, SettingsManager.GetInt(SettingsManager.KeyCaretScrollBuffer, EditorControl.DefaultCaretScrollBuffer));
        AddLabeledControl(panel, Strings.SettingsCaretScrollBuffer, _caretScrollBufferNum, Strings.SettingsCaretScrollBufferUnit, y, Strings.SettingsCaretScrollBufferDesc);

        return panel;
    }

    private Panel BuildAppearancePanel()
    {
        var panel = new Panel();

        // ── Header section (Dock.Top) ─────────────────────────────
        var headerPanel = new Panel { Dock = DockStyle.Top, AutoSize = true };
        int y = 0;

        // Theme
        _themeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
            Font = ControlFont(),
            BackColor = _theme.FindPanelBackground,
            ForeColor = _theme.EditorForeground,
        };
        foreach (string name in ThemeManager.Instance.ThemeNames)
            _themeCombo.Items.Add(name);
        string current = SettingsManager.GetString(SettingsManager.KeyTheme, _theme.Name);
        int tIdx = _themeCombo.Items.IndexOf(current);
        _themeCombo.SelectedIndex = tIdx >= 0 ? tIdx : 0;
        _themeCombo.SelectedIndexChanged += (_, _) => PopulateColorGrid();
        y = AddLabeledControl(headerPanel, Strings.SettingsTheme, _themeCombo, null, y, Strings.SettingsThemeDesc);

        // UI Language
        _uiLanguageCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
            Font = ControlFont(),
            BackColor = _theme.FindPanelBackground,
            ForeColor = _theme.EditorForeground,
        };
        var languages = LocalizationManager.GetAvailableLanguages();
        int langIdx = 0;
        for (int i = 0; i < languages.Count; i++)
        {
            _uiLanguageCombo.Items.Add(languages[i].DisplayName);
            if (languages[i].Code == LocalizationManager.CurrentLanguage)
                langIdx = i;
        }
        _uiLanguageCombo.SelectedIndex = langIdx;
        _uiLanguageCombo.Tag = languages; // store for lookup by index
        y = AddLabeledControl(headerPanel, Strings.SettingsUILanguage, _uiLanguageCombo, null, y, Strings.SettingsUILanguageDesc);

        // Recent files display style
        _recentFilesSeparatedCheck = CreateCheckBox(SettingsManager.GetBool(SettingsManager.KeyRecentFilesSeparated, true));
        y = AddLabeledControl(headerPanel, Strings.SettingsRecentFilesSeparated, _recentFilesSeparatedCheck, null, y, Strings.SettingsRecentFilesSeparatedDesc);

        // Separator
        var sep = new Panel
        {
            Location = new Point(0, y + 4),
            Size = new Size(480, 1),
            BackColor = _theme.TabBorder,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        headerPanel.Controls.Add(sep);
        y += 14;

        // Color Customization header
        var colorHeader = new Label
        {
            Text = Strings.SettingsColorCustomization,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = _theme.EditorForeground,
            AutoSize = true,
            Location = new Point(0, y),
        };
        headerPanel.Controls.Add(colorHeader);

        // Reset All Colors link
        var resetAllLink = new LinkLabel
        {
            Text = Strings.SettingsColorResetAll,
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            LinkColor = _theme.CaretColor,
            ActiveLinkColor = _theme.EditorForeground,
            Location = new Point(220, y + 2),
        };
        resetAllLink.Click += (_, _) =>
        {
            _pendingOverrides.Clear();
            PopulateColorGrid();
        };
        headerPanel.Controls.Add(resetAllLink);
        y += 28;

        headerPanel.Height = y;

        // ── Scrollable color grid (Dock.Fill) ─────────────────────
        _colorGridPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };

        // Add in reverse order: Fill first, then Top (WinForms dock order)
        panel.Controls.Add(_colorGridPanel);
        panel.Controls.Add(headerPanel);

        // Load existing overrides for the current theme
        LoadPendingOverrides();
        PopulateColorGrid();

        return panel;
    }

    private Panel BuildDisplayPanel()
    {
        var panel = new Panel { AutoScroll = true };
        int y = 0;

        // Caret Blink Rate
        _caretBlinkNum = CreateNumeric(100, 2000, 50, SettingsManager.GetInt(SettingsManager.KeyCaretBlinkRate, EditorControl.DefaultCaretBlinkRate));
        y = AddLabeledControl(panel, Strings.SettingsCaretBlinkRate, _caretBlinkNum, Strings.SettingsCaretBlinkRateUnit, y, Strings.SettingsCaretBlinkRateDesc);

        // Text Left Padding
        _textLeftPaddingNum = CreateNumeric(0, 40, 1, SettingsManager.GetInt(SettingsManager.KeyTextLeftPadding, EditorControl.DefaultTextLeftPadding));
        y = AddLabeledControl(panel, Strings.SettingsTextLeftPadding, _textLeftPaddingNum, Strings.SettingsTextLeftPaddingUnit, y, Strings.SettingsTextLeftPaddingDesc);

        // Line Spacing
        _lineSpacingNum = CreateNumeric(0, 20, 1, SettingsManager.GetInt(SettingsManager.KeyLineSpacing, EditorControl.DefaultLineSpacing));
        y = AddLabeledControl(panel, Strings.SettingsLineSpacing, _lineSpacingNum, Strings.SettingsLineSpacingUnit, y, Strings.SettingsLineSpacingDesc);

        // Min Zoom Font Size
        _minZoomFontNum = CreateNumeric(2, 20, 1, SettingsManager.GetInt(SettingsManager.KeyMinZoomFontSize, (int)EditorControl.DefaultMinZoomFontSize));
        y = AddLabeledControl(panel, Strings.SettingsMinZoomFontSize, _minZoomFontNum, Strings.SettingsMinZoomFontSizeUnit, y, Strings.SettingsMinZoomFontSizeDesc);

        // Whitespace Opacity
        _whitespaceOpacityNum = CreateNumeric(10, 255, 10, SettingsManager.GetInt(SettingsManager.KeyWhitespaceOpacity, EditorControl.DefaultWhitespaceOpacity));
        y = AddLabeledControl(panel, Strings.SettingsWhitespaceOpacity, _whitespaceOpacityNum, null, y, Strings.SettingsWhitespaceOpacityDesc);

        // Fold Indicator Opacity
        _foldIndicatorOpacityNum = CreateNumeric(10, 255, 10, SettingsManager.GetInt(SettingsManager.KeyFoldIndicatorOpacity, EditorControl.DefaultFoldIndicatorOpacity));
        y = AddLabeledControl(panel, Strings.SettingsFoldIndicatorOpacity, _foldIndicatorOpacityNum, null, y, Strings.SettingsFoldIndicatorOpacityDesc);

        // Gutter Padding Left
        _gutterPaddingLeftNum = CreateNumeric(0, 30, 1, SettingsManager.GetInt(SettingsManager.KeyGutterPaddingLeft, EditorControl.DefaultGutterPaddingLeft));
        y = AddLabeledControl(panel, Strings.SettingsGutterPaddingLeft, _gutterPaddingLeftNum, Strings.SettingsGutterPaddingLeftUnit, y, Strings.SettingsGutterPaddingLeftDesc);

        // Gutter Padding Right
        _gutterPaddingRightNum = CreateNumeric(0, 30, 1, SettingsManager.GetInt(SettingsManager.KeyGutterPaddingRight, EditorControl.DefaultGutterPaddingRight));
        y = AddLabeledControl(panel, Strings.SettingsGutterPaddingRight, _gutterPaddingRightNum, Strings.SettingsGutterPaddingRightUnit, y, Strings.SettingsGutterPaddingRightDesc);

        // Fold Button Size
        _foldButtonSizeNum = CreateNumeric(6, 24, 1, SettingsManager.GetInt(SettingsManager.KeyFoldButtonSize, EditorControl.DefaultFoldButtonSize));
        y = AddLabeledControl(panel, Strings.SettingsFoldButtonSize, _foldButtonSizeNum, Strings.SettingsFoldButtonSizeUnit, y, Strings.SettingsFoldButtonSizeDesc);

        // Bookmark Size
        _bookmarkSizeNum = CreateNumeric(4, 20, 1, SettingsManager.GetInt(SettingsManager.KeyBookmarkSize, EditorControl.DefaultBookmarkSize));
        y = AddLabeledControl(panel, Strings.SettingsBookmarkSize, _bookmarkSizeNum, Strings.SettingsBookmarkSizeUnit, y, Strings.SettingsBookmarkSizeDesc);

        // Tab Height
        _tabHeightNum = CreateNumeric(20, 60, 2, SettingsManager.GetInt(SettingsManager.KeyTabHeight, TabStrip.ConfigTabHeight));
        y = AddLabeledControl(panel, Strings.SettingsTabHeight, _tabHeightNum, Strings.SettingsTabHeightUnit, y, Strings.SettingsTabHeightDesc);

        // Max Tab Width
        _maxTabWidthNum = CreateNumeric(100, 500, 10, SettingsManager.GetInt(SettingsManager.KeyMaxTabWidth, TabStrip.ConfigMaxTabWidth));
        y = AddLabeledControl(panel, Strings.SettingsMaxTabWidth, _maxTabWidthNum, Strings.SettingsMaxTabWidthUnit, y, Strings.SettingsMaxTabWidthDesc);

        // Min Tab Width
        _minTabWidthNum = CreateNumeric(40, 200, 10, SettingsManager.GetInt(SettingsManager.KeyMinTabWidth, TabStrip.ConfigMinTabWidth));
        y = AddLabeledControl(panel, Strings.SettingsMinTabWidth, _minTabWidthNum, Strings.SettingsMinTabWidthUnit, y, Strings.SettingsMinTabWidthDesc);

        // Menu Item Padding
        _menuItemPaddingNum = CreateNumeric(0, 12, 1, SettingsManager.GetInt(SettingsManager.KeyMenuItemPadding, ThemedMenuRenderer.DefaultMenuItemPadding));
        y = AddLabeledControl(panel, Strings.SettingsMenuItemPadding, _menuItemPaddingNum, Strings.SettingsMenuItemPaddingUnit, y, Strings.SettingsMenuItemPaddingDesc);

        // Terminal Padding
        _terminalPaddingNum = CreateNumeric(0, 24, 1, SettingsManager.GetInt(SettingsManager.KeyTerminalPadding, TerminalPanel.DefaultTerminalPadding));
        AddLabeledControl(panel, Strings.SettingsTerminalPadding, _terminalPaddingNum, Strings.SettingsTerminalPaddingUnit, y, Strings.SettingsTerminalPaddingDesc);

        return panel;
    }

    private Panel BuildPerformancePanel()
    {
        var panel = new Panel { AutoScroll = true };
        int y = 0;

        // Large File Threshold (MB)
        _largeFileNum = CreateNumeric(1, 1000, 5, SettingsManager.GetInt(SettingsManager.KeyLargeFileThresholdMB, 10));
        y = AddLabeledControl(panel, Strings.SettingsLargeFileThreshold, _largeFileNum, Strings.SettingsLargeFileThresholdUnit, y, Strings.SettingsLargeFileThresholdDesc);

        // Folding Max File Size (MB)
        _foldingMaxNum = CreateNumeric(1, 500, 10, SettingsManager.GetInt(SettingsManager.KeyFoldingMaxFileSizeMB, 50));
        y = AddLabeledControl(panel, Strings.SettingsFoldingMaxFileSize, _foldingMaxNum, Strings.SettingsFoldingMaxFileSizeUnit, y, Strings.SettingsFoldingMaxFileSizeDesc);

        // Word Wrap Max File Size (MB)
        _wordWrapMaxNum = CreateNumeric(1, 5000, 10, SettingsManager.GetInt(SettingsManager.KeyWordWrapMaxFileSizeMB, 50));
        y = AddLabeledControl(panel, Strings.SettingsWordWrapMaxFileSize, _wordWrapMaxNum, Strings.SettingsWordWrapMaxFileSizeUnit, y, Strings.SettingsWordWrapMaxFileSizeDesc);

        // Max Recent Files
        _maxRecentFilesNum = CreateNumeric(5, 100, 5, SettingsManager.GetInt(SettingsManager.KeyMaxRecentFiles, RecentFilesManager.MaxRecentFiles));
        y = AddLabeledControl(panel, Strings.SettingsMaxRecentFiles, _maxRecentFilesNum, null, y, Strings.SettingsMaxRecentFilesDesc);

        // Search History Limit
        _searchHistoryNum = CreateNumeric(5, 100, 5, SettingsManager.GetInt(SettingsManager.KeySearchHistoryLimit, FindReplacePanel.ConfigMaxHistoryItems));
        y = AddLabeledControl(panel, Strings.SettingsSearchHistoryLimit, _searchHistoryNum, null, y, Strings.SettingsSearchHistoryLimitDesc);

        // Search Debounce
        _searchDebounceNum = CreateNumeric(50, 2000, 50, SettingsManager.GetInt(SettingsManager.KeySearchDebounce, EditorControl.DefaultSearchDebounce));
        y = AddLabeledControl(panel, Strings.SettingsSearchDebounce, _searchDebounceNum, Strings.SettingsSearchDebounceUnit, y, Strings.SettingsSearchDebounceDesc);

        // Auto-Save Interval
        _autoSaveIntervalNum = CreateNumeric(1, 300, 5, SettingsManager.GetInt(SettingsManager.KeyAutoSaveInterval, RecoveryManager.DefaultIntervalSeconds));
        AddLabeledControl(panel, Strings.SettingsAutoSaveInterval, _autoSaveIntervalNum, Strings.SettingsAutoSaveIntervalUnit, y, Strings.SettingsAutoSaveIntervalDesc);

        return panel;
    }

    private Panel BuildFilesPanel()
    {
        var panel = new Panel { AutoScroll = true };
        int y = 0;

        // ── Binary File Preferences header ───────────────────────
        var binaryHeader = new Label
        {
            Text = Strings.SettingsBinaryExtPrefs,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = _theme.EditorForeground,
            AutoSize = true,
            Location = new Point(0, y),
        };
        panel.Controls.Add(binaryHeader);
        y += binaryHeader.PreferredHeight + 4;

        var binaryDesc = new Label
        {
            Text = Strings.SettingsBinaryExtPrefsDesc,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = MutedColor(),
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Location = new Point(0, y),
        };
        panel.Controls.Add(binaryDesc);
        y += binaryDesc.PreferredHeight + 10;

        // ── ListView ─────────────────────────────────────────────
        _binaryExtPrefsList = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            GridLines = true,
            Location = new Point(0, y),
            Size = new Size(340, 160),
            Font = new Font("Segoe UI", 9.5f),
            BackColor = _theme.FindPanelBackground,
            ForeColor = _theme.EditorForeground,
        };
        _binaryExtPrefsList.Columns.Add(Strings.SettingsBinaryExtColExt, 140);
        _binaryExtPrefsList.Columns.Add(Strings.SettingsBinaryExtColMode, 180);
        _binaryExtPrefsList.MouseDoubleClick += OnBinaryExtPrefDoubleClick;
        PopulateBinaryExtPrefsList();
        panel.Controls.Add(_binaryExtPrefsList);

        // Buttons to the right of the list
        var deleteBtn = CreateButton(Strings.SettingsBinaryExtDelete);
        deleteBtn.Width = 100;
        deleteBtn.Location = new Point(350, y);
        deleteBtn.Click += OnBinaryExtDeleteClick;
        panel.Controls.Add(deleteBtn);

        var clearBtn = CreateButton(Strings.SettingsBinaryExtClearAll);
        clearBtn.Width = 100;
        clearBtn.Location = new Point(350, y + 40);
        clearBtn.Click += OnBinaryExtClearAllClick;
        panel.Controls.Add(clearBtn);

        return panel;
    }

    private Panel BuildSystemPanel()
    {
        var panel = new Panel { AutoScroll = true };
        int y = 0;

        // Checkbox with text inline (not using AddLabeledControl since the label is long)
        _contextMenuCheckBox = new CheckBox
        {
            Text = Strings.SettingsExplorerContextMenu,
            Checked = SettingsManager.IsExplorerContextMenuRegistered(),
            Font = LabelFont(),
            ForeColor = _theme.EditorForeground,
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Location = new Point(0, y + 2),
        };
        panel.Controls.Add(_contextMenuCheckBox);
        y += _contextMenuCheckBox.PreferredSize.Height + 6;

        var desc = new Label
        {
            Text = Strings.SettingsExplorerContextMenuDesc,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = MutedColor(),
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Location = new Point(18, y), // indent to align with checkbox text
        };
        panel.Controls.Add(desc);
        y += desc.PreferredHeight + 12;

        // ── New Explorer context menu ────────────────────────────
        var sep1 = new Panel
        {
            Location = new Point(0, y + 4),
            Size = new Size(480, 1),
            BackColor = _theme.TabBorder,
        };
        panel.Controls.Add(sep1);
        y += 20;

        _newExplorerContextMenuCheckBox = new CheckBox
        {
            Text = Strings.SettingsNewExplorerContextMenu,
            Checked = SettingsManager.IsNewExplorerContextMenuRegistered(),
            Font = LabelFont(),
            ForeColor = _theme.EditorForeground,
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Location = new Point(0, y + 2),
        };
        panel.Controls.Add(_newExplorerContextMenuCheckBox);
        y += _newExplorerContextMenuCheckBox.PreferredSize.Height + 6;

        var newExplorerDesc = new Label
        {
            Text = Strings.SettingsNewExplorerContextMenuDesc,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = MutedColor(),
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Location = new Point(18, y),
        };
        panel.Controls.Add(newExplorerDesc);
        y += newExplorerDesc.PreferredHeight + 12;

        // Separator
        var sep = new Panel
        {
            Location = new Point(0, y + 4),
            Size = new Size(480, 1),
            BackColor = _theme.TabBorder,
        };
        panel.Controls.Add(sep);
        y += 20;

        // Export Settings button
        var exportBtn = CreateButton(Strings.SettingsExportSettings);
        exportBtn.Width = 180;
        exportBtn.Location = new Point(0, y);
        exportBtn.Click += OnExportClick;
        panel.Controls.Add(exportBtn);
        y += 42;

        // Import Settings button
        var importBtn = CreateButton(Strings.SettingsImportSettings);
        importBtn.Width = 180;
        importBtn.Location = new Point(0, y);
        importBtn.Click += OnImportClick;
        panel.Controls.Add(importBtn);

        return panel;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────

    private void ShowCategory(int index)
    {
        for (int i = 0; i < _categoryPanels.Length; i++)
            _categoryPanels[i].Visible = (i == index);
    }

    private static Font ControlFont() => new("Segoe UI", 10f);
    private static Font LabelFont() => new("Segoe UI", 10f);

    private Color MutedColor() => Color.FromArgb(
        (_theme.EditorForeground.R + _theme.EditorBackground.R) / 2,
        (_theme.EditorForeground.G + _theme.EditorBackground.G) / 2,
        (_theme.EditorForeground.B + _theme.EditorBackground.B) / 2);

    private int AddLabeledControl(Panel parent, string labelText, Control control,
        string? unitText, int y, string? description = null)
    {
        const int controlX = 240;
        const int controlRowHeight = 32;

        var label = new Label
        {
            Text = labelText,
            Font = LabelFont(),
            ForeColor = _theme.EditorForeground,
            AutoSize = true,
            MaximumSize = new Size(controlX - 10, 0),
            Location = new Point(0, y + 6),
        };
        parent.Controls.Add(label);

        control.Location = new Point(controlX, y + 2);
        parent.Controls.Add(control);

        if (unitText is not null)
        {
            var unit = new Label
            {
                Text = unitText,
                Font = LabelFont(),
                ForeColor = MutedColor(),
                AutoSize = true,
                Location = new Point(control.Right + 6, y + 6),
            };
            parent.Controls.Add(unit);
        }

        int nextY = y + controlRowHeight;

        if (description is not null)
        {
            int descY = nextY + 4; // 4px gap below the control row
            var desc = new Label
            {
                Text = description,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = MutedColor(),
                AutoSize = true,
                MaximumSize = new Size(440, 0),
                Location = new Point(0, descY),
            };
            parent.Controls.Add(desc);
            nextY = descY + desc.PreferredHeight + 8; // 8px below description
        }
        else
        {
            nextY += 6; // small gap for rows without description
        }

        return nextY;
    }

    private NumericUpDown CreateNumeric(int min, int max, int increment, int value)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = increment,
            Value = Math.Clamp(value, min, max),
            Width = 100,
            Font = ControlFont(),
            BackColor = _theme.FindPanelBackground,
            ForeColor = _theme.EditorForeground,
        };
    }

    private CheckBox CreateCheckBox(bool isChecked)
    {
        return new CheckBox
        {
            Checked = isChecked,
            AutoSize = true,
            ForeColor = _theme.EditorForeground,
        };
    }

    private Button CreateButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = _theme.FindPanelBackground,
            ForeColor = _theme.EditorForeground,
            Font = new Font("Segoe UI", 9.5f),
            Size = new Size(90, 32),
        };
        btn.FlatAppearance.BorderColor = _theme.TabBorder;
        return btn;
    }

    private static void PopulateMonospaceFonts(ComboBox combo)
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);

        foreach (var family in FontFamily.Families)
        {
            if (!family.IsStyleAvailable(FontStyle.Regular)) continue;
            try
            {
                using var font = new Font(family, 10f, FontStyle.Regular, GraphicsUnit.Pixel);
                var sizeI = TextRenderer.MeasureText(g, "i", font, Size.Empty, TextFormatFlags.NoPadding);
                var sizeW = TextRenderer.MeasureText(g, "W", font, Size.Empty, TextFormatFlags.NoPadding);
                if (sizeI.Width == sizeW.Width)
                    combo.Items.Add(family.Name);
            }
            catch { }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Color grid
    // ────────────────────────────────────────────────────────────────────

    private void LoadPendingOverrides()
    {
        _pendingOverrides.Clear();
        string themeName = _themeCombo.SelectedItem as string ?? _theme.Name;
        string? json = SettingsManager.GetThemeOverrides(themeName);
        if (json is null) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    try
                    {
                        _pendingOverrides[prop.Name] = ColorTranslator.FromHtml(prop.Value.GetString()!);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private void PopulateColorGrid()
    {
        _colorGridPanel.Controls.Clear();
        string themeName = _themeCombo.SelectedItem as string ?? _theme.Name;
        ITheme? baseTheme = ThemeManager.Instance.GetBaseTheme(themeName) ?? _theme;

        int y = 0;
        string? lastGroup = null;

        foreach (var (group, property, displayName) in ColorEntries)
        {
            // Group header
            if (group != lastGroup)
            {
                lastGroup = group;
                string groupLabel = GetColorGroupLabel(group);
                var header = new Label
                {
                    Text = groupLabel,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = MutedColor(),
                    AutoSize = true,
                    Location = new Point(0, y + 2),
                };
                _colorGridPanel.Controls.Add(header);
                y += 22;
            }

            // Determine effective color
            Color baseColor = ThemeManager.GetThemeColor(baseTheme, property) ?? Color.Gray;
            bool hasOverride = _pendingOverrides.TryGetValue(property, out var overrideColor);
            Color effectiveColor = hasOverride ? overrideColor : baseColor;

            // Label
            var lbl = new Label
            {
                Text = displayName,
                Font = new Font("Segoe UI", 9f),
                ForeColor = _theme.EditorForeground,
                AutoSize = true,
                Location = new Point(10, y + 4),
            };
            _colorGridPanel.Controls.Add(lbl);

            // Color button
            string capturedProp = property;
            var colorBtn = new Button
            {
                Size = new Size(30, 20),
                Location = new Point(160, y + 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = effectiveColor,
            };
            colorBtn.FlatAppearance.BorderColor = _theme.TabBorder;
            colorBtn.Click += (_, _) =>
            {
                using var dlg = new ColorDialog { Color = colorBtn.BackColor, FullOpen = true };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _pendingOverrides[capturedProp] = dlg.Color;
                    PopulateColorGrid();
                }
            };
            _colorGridPanel.Controls.Add(colorBtn);

            // Hex label
            var hexLbl = new Label
            {
                Text = ColorToHex(effectiveColor),
                Font = new Font("Consolas", 9f),
                ForeColor = MutedColor(),
                AutoSize = true,
                Location = new Point(196, y + 4),
            };
            _colorGridPanel.Controls.Add(hexLbl);

            // Reset link (only if overridden)
            if (hasOverride)
            {
                var resetLink = new LinkLabel
                {
                    Text = Strings.SettingsColorReset,
                    Font = new Font("Segoe UI", 8.5f),
                    AutoSize = true,
                    LinkColor = _theme.CaretColor,
                    ActiveLinkColor = _theme.EditorForeground,
                    Location = new Point(270, y + 4),
                };
                resetLink.Click += (_, _) =>
                {
                    _pendingOverrides.Remove(capturedProp);
                    PopulateColorGrid();
                };
                _colorGridPanel.Controls.Add(resetLink);
            }

            y += 26;
        }

        // Force the scroll range to cover all content.
        _colorGridPanel.AutoScrollMinSize = new Size(0, y);
    }

    private static string GetColorGroupLabel(string group) => group switch
    {
        "Editor" => Strings.SettingsColorGroupEditor,
        "Gutter" => Strings.SettingsColorGroupGutter,
        "Tabs" => Strings.SettingsColorGroupTabs,
        "StatusBar" => Strings.SettingsColorGroupStatusBar,
        "FindPanel" => Strings.SettingsColorGroupFindPanel,
        "Menus" => Strings.SettingsColorGroupMenus,
        "ScrollBar" => Strings.SettingsColorGroupScrollBar,
        "Diff" => Strings.SettingsColorGroupDiff,
        "Other" => Strings.SettingsColorGroupOther,
        _ => group,
    };

    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private string? SerializePendingOverrides()
    {
        if (_pendingOverrides.Count == 0) return null;
        var dict = new Dictionary<string, string>();
        foreach (var (key, color) in _pendingOverrides)
            dict[key] = ColorToHex(color);
        return JsonSerializer.Serialize(dict);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Event handlers
    // ────────────────────────────────────────────────────────────────────

    private void OnOkClick(object? sender, EventArgs e)
    {
        // ── Editor ──────────────────────────────────────────────────
        if (_fontFamilyCombo.SelectedItem is string fontName)
            SettingsManager.SetString(SettingsManager.KeyFontFamily, fontName);
        SettingsManager.SetInt(SettingsManager.KeyFontSize, (int)_fontSizeNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyTabWidth, (int)_tabWidthNum.Value);
        SettingsManager.SetBool(SettingsManager.KeyAutoIndent, _autoIndentCheck.Checked);
        SettingsManager.SetInt(SettingsManager.KeyScrollSpeed, (int)_scrollSpeedNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyCaretScrollBuffer, (int)_caretScrollBufferNum.Value);

        // ── Appearance ──────────────────────────────────────────────
        if (_themeCombo.SelectedItem is string themeName)
            SettingsManager.SetString(SettingsManager.KeyTheme, themeName);

        SettingsManager.SetBool(SettingsManager.KeyRecentFilesSeparated, _recentFilesSeparatedCheck.Checked);

        // UI Language
        var langs = _uiLanguageCombo.Tag as List<(string Code, string DisplayName)>;
        if (langs is not null && _uiLanguageCombo.SelectedIndex >= 0 && _uiLanguageCombo.SelectedIndex < langs.Count)
        {
            string code = langs[_uiLanguageCombo.SelectedIndex].Code;
            if (code != LocalizationManager.CurrentLanguage)
                LocalizationManager.LoadLanguage(code);
        }

        // Theme overrides
        string selectedTheme = _themeCombo.SelectedItem as string ?? _theme.Name;
        SettingsManager.SetThemeOverrides(selectedTheme, SerializePendingOverrides());

        // ── Display ─────────────────────────────────────────────────
        SettingsManager.SetInt(SettingsManager.KeyCaretBlinkRate, (int)_caretBlinkNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyTextLeftPadding, (int)_textLeftPaddingNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyLineSpacing, (int)_lineSpacingNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyMinZoomFontSize, (int)_minZoomFontNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyWhitespaceOpacity, (int)_whitespaceOpacityNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyFoldIndicatorOpacity, (int)_foldIndicatorOpacityNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyGutterPaddingLeft, (int)_gutterPaddingLeftNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyGutterPaddingRight, (int)_gutterPaddingRightNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyFoldButtonSize, (int)_foldButtonSizeNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyBookmarkSize, (int)_bookmarkSizeNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyTabHeight, (int)_tabHeightNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyMaxTabWidth, (int)_maxTabWidthNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyMinTabWidth, (int)_minTabWidthNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyMenuItemPadding, (int)_menuItemPaddingNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyTerminalPadding, (int)_terminalPaddingNum.Value);

        // ── Performance ─────────────────────────────────────────────
        SettingsManager.SetInt(SettingsManager.KeyLargeFileThresholdMB, (int)_largeFileNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyFoldingMaxFileSizeMB, (int)_foldingMaxNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyWordWrapMaxFileSizeMB, (int)_wordWrapMaxNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyMaxRecentFiles, (int)_maxRecentFilesNum.Value);
        SettingsManager.SetInt(SettingsManager.KeySearchHistoryLimit, (int)_searchHistoryNum.Value);
        SettingsManager.SetInt(SettingsManager.KeySearchDebounce, (int)_searchDebounceNum.Value);
        SettingsManager.SetInt(SettingsManager.KeyAutoSaveInterval, (int)_autoSaveIntervalNum.Value);

        // ── System ──────────────────────────────────────────────────
        bool wantRegistered = _contextMenuCheckBox.Checked;
        bool isRegistered = SettingsManager.IsExplorerContextMenuRegistered();
        if (wantRegistered && !isRegistered)
            SettingsManager.RegisterExplorerContextMenu();
        else if (!wantRegistered && isRegistered)
            SettingsManager.UnregisterExplorerContextMenu();

        // Windows primary context menu
        bool wantNewExplorer = _newExplorerContextMenuCheckBox.Checked;
        bool isNewExplorer = SettingsManager.IsNewExplorerContextMenuRegistered();
        if (wantNewExplorer != isNewExplorer)
        {
            Cursor = Cursors.WaitCursor;
            string? error = wantNewExplorer
                ? SettingsManager.RegisterNewExplorerContextMenu()
                : SettingsManager.UnregisterNewExplorerContextMenu();
            Cursor = Cursors.Default;

            if (error is not null)
            {
                MessageBox.Show(
                    string.Format(Strings.SettingsNewExplorerContextMenuError, error),
                    Strings.SettingsTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                var restart = MessageBox.Show(
                    Strings.SettingsNewExplorerRestartExplorer,
                    Strings.SettingsTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (restart == DialogResult.Yes)
                    SettingsManager.RestartExplorer();
            }
        }
    }

    private void OnResetClick(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            Strings.SettingsResetConfirm,
            Strings.SettingsTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        SettingsManager.ResetToDefaults();

        // Reload form values with defaults.
        // Editor
        int fIdx = _fontFamilyCombo.Items.IndexOf("Consolas");
        if (fIdx >= 0) _fontFamilyCombo.SelectedIndex = fIdx;
        _fontSizeNum.Value = 11;
        _tabWidthNum.Value = 4;
        _autoIndentCheck.Checked = true;
        _scrollSpeedNum.Value = 3;
        _caretScrollBufferNum.Value = 4;

        // Appearance
        int tIdx = _themeCombo.Items.IndexOf("Dark");
        if (tIdx >= 0) _themeCombo.SelectedIndex = tIdx;
        _uiLanguageCombo.SelectedIndex = 0; // English
        _recentFilesSeparatedCheck.Checked = true;
        _pendingOverrides.Clear();
        PopulateColorGrid();

        // Display
        _caretBlinkNum.Value = 500;
        _textLeftPaddingNum.Value = 6;
        _lineSpacingNum.Value = 2;
        _minZoomFontNum.Value = 6;
        _whitespaceOpacityNum.Value = 100;
        _foldIndicatorOpacityNum.Value = 60;
        _gutterPaddingLeftNum.Value = 8;
        _gutterPaddingRightNum.Value = 12;
        _foldButtonSizeNum.Value = 10;
        _bookmarkSizeNum.Value = 8;
        _tabHeightNum.Value = 30;
        _maxTabWidthNum.Value = 220;
        _minTabWidthNum.Value = 80;
        _menuItemPaddingNum.Value = ThemedMenuRenderer.DefaultMenuItemPadding;
        _terminalPaddingNum.Value = TerminalPanel.DefaultTerminalPadding;

        // Performance
        _largeFileNum.Value = 10;
        _foldingMaxNum.Value = 50;
        _maxRecentFilesNum.Value = 20;
        _searchHistoryNum.Value = 25;
        _searchDebounceNum.Value = 300;
        _autoSaveIntervalNum.Value = RecoveryManager.DefaultIntervalSeconds;

        _contextMenuCheckBox.Checked = SettingsManager.IsExplorerContextMenuRegistered();
        _newExplorerContextMenuCheckBox.Checked = SettingsManager.IsNewExplorerContextMenuRegistered();

        // Binary file preferences
        PopulateBinaryExtPrefsList();
    }

    private static string BinaryModeDisplayName(string mode) =>
        mode == "hex" ? Strings.SettingsBinaryExtModeHex : Strings.SettingsBinaryExtModeText;

    private void PopulateBinaryExtPrefsList()
    {
        _binaryExtPrefsList.Items.Clear();
        foreach (var (ext, mode) in SettingsManager.GetAllBinaryExtPrefs())
        {
            var item = new ListViewItem([ext, BinaryModeDisplayName(mode)])
            {
                Tag = mode // store raw value for toggling
            };
            _binaryExtPrefsList.Items.Add(item);
        }
    }

    private void OnBinaryExtPrefDoubleClick(object? sender, MouseEventArgs e)
    {
        var hit = _binaryExtPrefsList.HitTest(e.Location);
        if (hit.Item is null) return;

        // Only toggle when clicking the mode column (sub-item index 1).
        // SubItem 0 = extension column — ignore double-clicks there.
        if (hit.SubItem != hit.Item.SubItems[1]) return;

        string ext = hit.Item.Text;
        string currentMode = hit.Item.Tag as string ?? "hex";
        string newMode = currentMode == "hex" ? "text" : "hex";

        SettingsManager.SetBinaryExtPref(ext, newMode);
        hit.Item.Tag = newMode;
        hit.Item.SubItems[1].Text = BinaryModeDisplayName(newMode);
    }

    private void OnBinaryExtDeleteClick(object? sender, EventArgs e)
    {
        if (_binaryExtPrefsList.SelectedItems.Count == 0) return;
        string ext = _binaryExtPrefsList.SelectedItems[0].Text;
        SettingsManager.SetBinaryExtPref(ext, null);
        PopulateBinaryExtPrefsList();
    }

    private void OnBinaryExtClearAllClick(object? sender, EventArgs e)
    {
        if (_binaryExtPrefsList.Items.Count == 0) return;

        var result = MessageBox.Show(
            Strings.SettingsBinaryExtClearConfirm,
            Strings.SettingsTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        SettingsManager.ClearAllBinaryExtPrefs();
        PopulateBinaryExtPrefsList();
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = $"{Strings.SettingsJsonFilter} (*.json)|*.json",
            DefaultExt = "json",
            FileName = "bascanka-settings.json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string json = SettingsManager.ExportToJson();
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show(Strings.SettingsExportSuccess, Strings.SettingsTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.SettingsImportError, ex.Message),
                Strings.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportClick(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = $"{Strings.SettingsJsonFilter} (*.json)|*.json",
            DefaultExt = "json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string json = File.ReadAllText(dlg.FileName);
            SettingsManager.ImportFromJson(json);
            MessageBox.Show(Strings.SettingsImportSuccess, Strings.SettingsTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.SettingsImportError, ex.Message),
                Strings.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
