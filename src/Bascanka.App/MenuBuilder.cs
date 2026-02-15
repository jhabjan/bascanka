using Bascanka.Core.Syntax;
using Bascanka.Editor.Themes;

namespace Bascanka.App;

/// <summary>
/// Constructs the main menu bar for the Bascanka editor.
/// Each top-level menu is created with its items, keyboard shortcuts,
/// and event handlers wired to <see cref="MainForm"/> methods.
/// </summary>
public sealed class MenuBuilder
{
    private ToolStripMenuItem? _recentFilesMenu;
    private ToolStripMenuItem? _pluginsMenu;
    private ToolStripMenuItem? _pluginsMenuItem;
    private ToolStripMenuItem? _languageMenu;

    // File menu items for enable/disable toggling.
    private ToolStripMenuItem? _saveItem;
    private ToolStripMenuItem? _saveAsItem;
    private ToolStripMenuItem? _saveAllItem;
    private ToolStripMenuItem? _printItem;
    private ToolStripMenuItem? _printPreviewItem;

    // Edit menu items for enable/disable toggling.
    private ToolStripMenuItem? _undoItem;
    private ToolStripMenuItem? _redoItem;
    private ToolStripMenuItem? _cutItem;
    private ToolStripMenuItem? _copyItem;
    private ToolStripMenuItem? _pasteItem;
    private ToolStripMenuItem? _deleteItem;
    private ToolStripMenuItem? _findItem;
    private ToolStripMenuItem? _replaceItem;
    private ToolStripMenuItem? _findInFilesItem;
    private ToolStripMenuItem? _goToLineItem;
    private ToolStripMenuItem? _selectAllItem;

    // Encoding menu items for checkmark toggling.
    private ToolStripMenuItem? _encodingMenu;
    private ToolStripMenuItem? _lineEndingsMenu;

    // Text menu — the whole menu is toggled based on hasTab.
    private ToolStripMenuItem? _textMenu;
    // Text menu items that require a selection.
    private readonly List<ToolStripMenuItem> _textSelectionItems = new();

    // View menu items for checkmark toggling.
    private ToolStripMenuItem? _wordWrapItem;
    private ToolStripMenuItem? _showWhitespaceItem;
    private ToolStripMenuItem? _lineNumbersItem;
    private ToolStripMenuItem? _findResultsItem;

    // Tools menu items for enable/disable toggling.
    private ToolStripMenuItem? _hexEditorItem;

    // Macro menu items for enable/disable toggling.
    private ToolStripMenuItem? _recordMacroItem;
    private ToolStripMenuItem? _stopRecordingItem;
    private ToolStripMenuItem? _playMacroItem;
    private ToolStripMenuItem? _macroManagerItem;

    /// <summary>
    /// Builds the full menu strip and populates it with all menus.
    /// </summary>
    public void BuildMenu(MainForm form, MenuStrip menuStrip)
    {
        menuStrip.Items.Clear();
        menuStrip.Items.Add(BuildFileMenu(form));
        menuStrip.Items.Add(BuildEditMenu(form));
        menuStrip.Items.Add(BuildTextMenu(form));
        menuStrip.Items.Add(BuildViewMenu(form));
        menuStrip.Items.Add(BuildEncodingMenu(form));
        menuStrip.Items.Add(BuildLanguageMenu(form));
        menuStrip.Items.Add(BuildToolsMenu(form));
        _pluginsMenuItem = BuildPluginsMenu(form);
        menuStrip.Items.Add(_pluginsMenuItem);
        menuStrip.Items.Add(BuildHelpMenu(form));
    }

    /// <summary>
    /// Refreshes the "Open Recent" submenu with the latest MRU list.
    /// </summary>
    public void RefreshRecentFilesMenu(MainForm form)
    {
        if (_recentFilesMenu is null) return;

        _recentFilesMenu.DropDownItems.Clear();

        IReadOnlyList<string> recent = form.RecentFilesManager.GetRecentFiles();
        if (recent.Count == 0)
        {
            var empty = new ToolStripMenuItem(Strings.NoRecentFiles) { Enabled = false };
            _recentFilesMenu.DropDownItems.Add(empty);
            return;
        }

        foreach (string path in recent)
        {
            string displayPath = path;
            string capturedPath = path;
            var item = new ToolStripMenuItem(displayPath);
            item.Click += (_, _) => form.OpenFile(capturedPath);
            _recentFilesMenu.DropDownItems.Add(item);
        }

        _recentFilesMenu.DropDownItems.Add(new ToolStripSeparator());
        var clearItem = new ToolStripMenuItem(Strings.ClearRecentFiles);
        clearItem.Click += (_, _) =>
        {
            form.RecentFilesManager.ClearRecentFiles();
            RefreshRecentFilesMenu(form);
        };
        _recentFilesMenu.DropDownItems.Add(clearItem);
    }

    /// <summary>
    /// Updates the checkmark on Language menu items to reflect the active document's language.
    /// </summary>
    public void RefreshLanguageMenu(MainForm form)
    {
        if (_languageMenu is null) return;

        string? customProfileName = form.ActiveTab?.Editor.CustomProfileName;
        bool isCustomActive = customProfileName is not null;
        string currentLangId = form.ActiveTab?.Editor.CurrentLexer?.LanguageId ?? "plaintext";

        foreach (ToolStripItem item in _languageMenu.DropDownItems)
        {
            if (item is not ToolStripMenuItem menuItem) continue;

            // Skip the Custom submenu — it handles its own checkmarks dynamically.
            if (menuItem.Text == Strings.MenuCustomHighlighting)
                continue;

            // "Plain Text" item maps to "plaintext".
            if (menuItem.Text == Strings.PlainText)
            {
                menuItem.Checked = !isCustomActive &&
                    string.Equals(currentLangId, "plaintext", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // Match by formatted display name → language id.
                string itemLangId = ResolveLanguageId(menuItem.Text ?? string.Empty);
                menuItem.Checked = !isCustomActive &&
                    string.Equals(itemLangId, currentLangId, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Refreshes the Plugins menu with dynamic plugin items.
    /// </summary>
    public void RefreshPluginsMenu(MainForm form)
    {
        if (_pluginsMenu is null) return;

        // Remove dynamic items (everything after the separator).
        while (_pluginsMenu.DropDownItems.Count > 2)
            _pluginsMenu.DropDownItems.RemoveAt(2);

        foreach (string name in form.PluginHost.LoadedPluginNames)
        {
            var item = new ToolStripMenuItem(name) { Enabled = false };
            _pluginsMenu.DropDownItems.Add(item);
        }
    }

    // ── File Menu ────────────────────────────────────────────────────

    private ToolStripMenuItem BuildFileMenu(MainForm form)
    {
        var menu = new ToolStripMenuItem(Strings.MenuFile);

        menu.DropDownItems.Add(MakeItem(Strings.MenuNew, Keys.Control | Keys.N,
            () => form.NewDocument()));

        menu.DropDownItems.Add(MakeItem(Strings.MenuOpen, Keys.Control | Keys.O,
            () => form.OpenFile()));

        _recentFilesMenu = new ToolStripMenuItem(Strings.MenuOpenRecent);
        RefreshRecentFilesMenu(form);
        menu.DropDownItems.Add(_recentFilesMenu);

        menu.DropDownItems.Add(new ToolStripSeparator());

        _saveItem = MakeItem(Strings.MenuSave, Keys.Control | Keys.S,
            () => form.SaveCurrentDocument());
        menu.DropDownItems.Add(_saveItem);

        _saveAsItem = MakeItem(Strings.MenuSaveAs, Keys.Control | Keys.Shift | Keys.S,
            () => form.SaveAs());
        menu.DropDownItems.Add(_saveAsItem);

        _saveAllItem = MakeItem(Strings.MenuSaveAll, Keys.None,
            () => form.SaveAll());
        menu.DropDownItems.Add(_saveAllItem);

        menu.DropDownItems.Add(new ToolStripSeparator());

        _printItem = MakeItem(Strings.MenuPrint, Keys.Control | Keys.P,
            () => form.PrintDocument());
        menu.DropDownItems.Add(_printItem);

        _printPreviewItem = MakeItem(Strings.MenuPrintPreview, Keys.None,
            () => form.PrintPreviewDocument());
        menu.DropDownItems.Add(_printPreviewItem);

        menu.DropDownItems.Add(new ToolStripSeparator());

        menu.DropDownItems.Add(MakeItem(Strings.MenuExit, Keys.Alt | Keys.F4,
            () => form.Close()));

        return menu;
    }

    // ── Edit Menu ────────────────────────────────────────────────────

    private ToolStripMenuItem BuildEditMenu(MainForm form)
    {
        var menu = new ToolStripMenuItem(Strings.MenuEdit);

        _undoItem = MakeItem(Strings.MenuUndo, Keys.Control | Keys.Z,
            () => form.ActiveTab?.Editor.Undo());
        menu.DropDownItems.Add(_undoItem);

        _redoItem = MakeItem(Strings.MenuRedo, Keys.Control | Keys.Y,
            () => form.ActiveTab?.Editor.Redo());
        menu.DropDownItems.Add(_redoItem);

        menu.DropDownItems.Add(new ToolStripSeparator());

        _cutItem = MakeItem(Strings.MenuCut, Keys.Control | Keys.X,
            () => form.ActiveTab?.Editor.Cut());
        menu.DropDownItems.Add(_cutItem);

        _copyItem = MakeItem(Strings.MenuCopy, Keys.Control | Keys.C,
            () => form.ActiveTab?.Editor.Copy());
        menu.DropDownItems.Add(_copyItem);

        _pasteItem = MakeItem(Strings.MenuPaste, Keys.Control | Keys.V,
            () => form.ActiveTab?.Editor.Paste());
        menu.DropDownItems.Add(_pasteItem);

        _deleteItem = MakeItem(Strings.MenuDelete, Keys.Delete,
            () => form.ActiveTab?.Editor.DeleteSelection());
        menu.DropDownItems.Add(_deleteItem);

        menu.DropDownItems.Add(new ToolStripSeparator());

        _selectAllItem = MakeItem(Strings.MenuSelectAll, Keys.Control | Keys.A,
            () => form.ActiveTab?.Editor.SelectAll());
        menu.DropDownItems.Add(_selectAllItem);

        menu.DropDownItems.Add(new ToolStripSeparator());

        _findItem = MakeItem(Strings.MenuFind, Keys.Control | Keys.F,
            () => form.ShowFind());
        menu.DropDownItems.Add(_findItem);

        _replaceItem = MakeItem(Strings.MenuReplace, Keys.Control | Keys.H,
            () => form.ShowFindReplace());
        menu.DropDownItems.Add(_replaceItem);

        _findInFilesItem = MakeItem(Strings.MenuFindInFiles, Keys.Control | Keys.Shift | Keys.F,
            () => form.ShowFind());
        menu.DropDownItems.Add(_findInFilesItem);

        _goToLineItem = MakeItem(Strings.MenuGoToLine, Keys.Control | Keys.G,
            () => form.ShowGoToLine());
        menu.DropDownItems.Add(_goToLineItem);

        return menu;
    }

    // ── Text Menu ────────────────────────────────────────────────────

    private ToolStripMenuItem BuildTextMenu(MainForm form)
    {
        _textMenu = new ToolStripMenuItem(Strings.MenuText);
        _textSelectionItems.Clear();

        // ── Case conversions (require selection) ────────────────────
        var caseMenu = new ToolStripMenuItem(Strings.MenuCaseConversion);
        caseMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuUpperCase,
            () => form.TransformSelection(TextTransformations.ToUpperCase)));
        caseMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuLowerCase,
            () => form.TransformSelection(TextTransformations.ToLowerCase)));
        caseMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuTitleCase,
            () => form.TransformSelection(TextTransformations.ToTitleCase)));
        caseMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuSwapCase,
            () => form.TransformSelection(TextTransformations.SwapCase)));
        _textMenu.DropDownItems.Add(caseMenu);

        // ── Encoding (require selection) ────────────────────────────
        var encMenu = new ToolStripMenuItem(Strings.MenuTextEncoding);
        encMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuBase64Encode,
            () => form.TransformSelection(TextTransformations.Base64Encode)));
        encMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuBase64Decode,
            () => form.TransformSelection(TextTransformations.Base64Decode)));
        encMenu.DropDownItems.Add(new ToolStripSeparator());
        encMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuUrlEncode,
            () => form.TransformSelection(TextTransformations.UrlEncode)));
        encMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuUrlDecode,
            () => form.TransformSelection(TextTransformations.UrlDecode)));
        encMenu.DropDownItems.Add(new ToolStripSeparator());
        encMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuHtmlEncode,
            () => form.TransformSelection(TextTransformations.HtmlEncode)));
        encMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuHtmlDecode,
            () => form.TransformSelection(TextTransformations.HtmlDecode)));
        _textMenu.DropDownItems.Add(encMenu);

        _textMenu.DropDownItems.Add(new ToolStripSeparator());

        // ── Line operations (require selection) ─────────────────────
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuSortLinesAsc,
            () => form.TransformSelection(TextTransformations.SortLinesAscending)));
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuSortLinesDesc,
            () => form.TransformSelection(TextTransformations.SortLinesDescending)));
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuRemoveDuplicateLines,
            () => form.TransformSelection(TextTransformations.RemoveDuplicateLines)));
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuReverseLines,
            () => form.TransformSelection(TextTransformations.ReverseLines)));

        _textMenu.DropDownItems.Add(new ToolStripSeparator());

        // ── Whitespace operations (require selection) ───────────────
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuTrimTrailingWhitespace,
            () => form.TransformSelection(TextTransformations.TrimTrailingWhitespace)));
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuTrimLeadingWhitespace,
            () => form.TransformSelection(TextTransformations.TrimLeadingWhitespace)));
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuCompactWhitespace,
            () => form.TransformSelection(TextTransformations.CompactWhitespace)));

        _textMenu.DropDownItems.Add(new ToolStripSeparator());

        // ── Tab/space conversion (require selection) ────────────────
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuTabsToSpaces,
            () => form.TransformSelection(TextTransformations.TabsToSpaces)));
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuSpacesToTabs,
            () => form.TransformSelection(TextTransformations.SpacesToTabs)));

        _textMenu.DropDownItems.Add(new ToolStripSeparator());

        // ── Other (require selection) ───────────────────────────────
        _textMenu.DropDownItems.Add(MakeSelectionItem(Strings.MenuReverseText,
            () => form.TransformSelection(TextTransformations.ReverseText)));

        _textMenu.DropDownItems.Add(new ToolStripSeparator());

        // ── JSON (works on selection or entire document) ─────────────
        var jsonMenu = new ToolStripMenuItem(Strings.MenuJson);
        jsonMenu.DropDownItems.Add(MakeItem(Strings.MenuJsonFormat, Keys.None,
            () => form.TransformSelectionOrDocument(TextTransformations.JsonFormat)));
        jsonMenu.DropDownItems.Add(MakeItem(Strings.MenuJsonMinimize, Keys.None,
            () => form.TransformSelectionOrDocument(TextTransformations.JsonMinimize)));
        _textMenu.DropDownItems.Add(jsonMenu);

        return _textMenu;
    }

    /// <summary>Creates a menu item tracked for selection-dependent enabling.</summary>
    private ToolStripMenuItem MakeSelectionItem(string text, Action onClick)
    {
        var item = MakeItem(text, Keys.None, onClick);
        _textSelectionItems.Add(item);
        return item;
    }

    // ── View Menu ────────────────────────────────────────────────────

    private ToolStripMenuItem BuildViewMenu(MainForm form)
    {
        var menu = new ToolStripMenuItem(Strings.MenuView);

        _wordWrapItem = MakeItem(Strings.MenuWordWrap, Keys.None,
            () => form.ToggleWordWrap());
        menu.DropDownItems.Add(_wordWrapItem);

        _showWhitespaceItem = MakeItem(Strings.MenuShowWhitespace, Keys.None,
            () => form.ToggleShowWhitespace());
        menu.DropDownItems.Add(_showWhitespaceItem);

        _lineNumbersItem = MakeItem(Strings.MenuLineNumbers, Keys.None,
            () => form.ToggleLineNumbers());
        menu.DropDownItems.Add(_lineNumbersItem);

        menu.DropDownItems.Add(new ToolStripSeparator());

        menu.DropDownItems.Add(MakeItem(Strings.MenuToggleFold, Keys.Control | Keys.Shift | Keys.OemOpenBrackets,
            () => form.ToggleFoldAtCaret()));
        menu.DropDownItems.Add(MakeItem(Strings.MenuFoldAll, Keys.Control | Keys.Shift | Keys.OemMinus,
            () => form.FoldAll()));
        menu.DropDownItems.Add(MakeItem(Strings.MenuUnfoldAll, Keys.Control | Keys.Shift | Keys.Oemplus,
            () => form.UnfoldAll()));

        menu.DropDownItems.Add(new ToolStripSeparator());

        menu.DropDownItems.Add(MakeItem(Strings.MenuZoomIn, Keys.Control | Keys.Oemplus,
            () => form.ZoomIn()));

        menu.DropDownItems.Add(MakeItem(Strings.MenuZoomOut, Keys.Control | Keys.OemMinus,
            () => form.ZoomOut()));

        menu.DropDownItems.Add(MakeItem(Strings.MenuResetZoom, Keys.Control | Keys.D0,
            () => form.ResetZoom()));

        menu.DropDownItems.Add(new ToolStripSeparator());

        menu.DropDownItems.Add(MakeItem(Strings.MenuFullScreen, Keys.F11,
            () => form.ToggleFullScreen()));

        menu.DropDownItems.Add(new ToolStripSeparator());

        menu.DropDownItems.Add(MakeItem(Strings.MenuSymbolList, Keys.None,
            () => form.ToggleSymbolList()));

        _findResultsItem = MakeItem(Strings.MenuFindResults, Keys.None,
            () => { form.ToggleFindResults(); UpdateMenuState(form); });
        menu.DropDownItems.Add(_findResultsItem);

        return menu;
    }

    // ── Encoding Menu ────────────────────────────────────────────────

    private ToolStripMenuItem BuildEncodingMenu(MainForm form)
    {
        _encodingMenu = new ToolStripMenuItem(Strings.MenuEncoding);

        _encodingMenu.DropDownItems.Add(MakeItem("UTF-8", Keys.None,
            () => form.SetEncoding(new System.Text.UTF8Encoding(false), false)));

        _encodingMenu.DropDownItems.Add(MakeItem("UTF-8 with BOM", Keys.None,
            () => form.SetEncoding(new System.Text.UTF8Encoding(true), true)));

        _encodingMenu.DropDownItems.Add(MakeItem("UTF-16 LE", Keys.None,
            () => form.SetEncoding(System.Text.Encoding.Unicode, true)));

        _encodingMenu.DropDownItems.Add(MakeItem("UTF-16 BE", Keys.None,
            () => form.SetEncoding(System.Text.Encoding.BigEndianUnicode, true)));

        _encodingMenu.DropDownItems.Add(MakeItem("ASCII", Keys.None,
            () => form.SetEncoding(System.Text.Encoding.ASCII, false)));

        _encodingMenu.DropDownItems.Add(MakeItem("Windows-1252", Keys.None,
            () => form.SetEncoding(System.Text.Encoding.GetEncoding(1252), false)));

        _encodingMenu.DropDownItems.Add(MakeItem("ISO-8859-1", Keys.None,
            () => form.SetEncoding(System.Text.Encoding.GetEncoding("iso-8859-1"), false)));

        _encodingMenu.DropDownItems.Add(MakeItem("Chinese Simplified (GB2312)", Keys.None,
            () => form.SetEncoding(System.Text.Encoding.GetEncoding("GB2312"), false)));

        _encodingMenu.DropDownItems.Add(new ToolStripSeparator());

        // Line endings submenu.
        _lineEndingsMenu = new ToolStripMenuItem(Strings.MenuConvertLineEndings);
        _lineEndingsMenu.DropDownItems.Add(MakeItem("CRLF (Windows)", Keys.None,
            () => form.SetLineEnding("CRLF")));
        _lineEndingsMenu.DropDownItems.Add(MakeItem("LF (Unix/macOS)", Keys.None,
            () => form.SetLineEnding("LF")));
        _lineEndingsMenu.DropDownItems.Add(MakeItem("CR (Classic Mac)", Keys.None,
            () => form.SetLineEnding("CR")));
        _encodingMenu.DropDownItems.Add(_lineEndingsMenu);

        return _encodingMenu;
    }

    /// <summary>
    /// Updates the checkmarks on the Encoding and Line Endings menus.
    /// </summary>
    public void RefreshEncodingMenu(MainForm form)
    {
        if (_encodingMenu is null) return;

        var tab = form.ActiveTab;
        var enc = tab?.Editor.EncodingManager;
        string currentName = GetEncodingDisplayName(enc);
        string currentLineEnding = tab?.Editor.LineEnding ?? "CRLF";

        // Encoding items (skip separator and line endings submenu).
        foreach (ToolStripItem item in _encodingMenu.DropDownItems)
        {
            if (item is ToolStripMenuItem menuItem && item != _lineEndingsMenu)
                menuItem.Checked = string.Equals(menuItem.Text, currentName, StringComparison.Ordinal);
        }

        // Line ending items.
        if (_lineEndingsMenu is not null)
        {
            foreach (ToolStripItem item in _lineEndingsMenu.DropDownItems)
            {
                if (item is ToolStripMenuItem menuItem)
                    menuItem.Checked = (menuItem.Text ?? "").StartsWith(currentLineEnding, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string GetEncodingDisplayName(Bascanka.Core.Encoding.EncodingManager? enc)
    {
        if (enc is null) return "UTF-8";

        string name = enc.CurrentEncoding.WebName.ToUpperInvariant();
        bool hasBom = enc.HasBom;

        return name switch
        {
            "UTF-8" when hasBom => "UTF-8 with BOM",
            "UTF-8" => "UTF-8",
            "UTF-16" or "UTF-16LE" => "UTF-16 LE",
            "UTF-16BE" => "UTF-16 BE",
            "US-ASCII" => "ASCII",
            "WINDOWS-1252" => "Windows-1252",
            "ISO-8859-1" => "ISO-8859-1",
            "GB2312" => "Chinese Simplified (GB2312)",
            _ => enc.CurrentEncoding.EncodingName,
        };
    }

    // ── Language Menu ────────────────────────────────────────────────

    private ToolStripMenuItem BuildLanguageMenu(MainForm form)
    {
        _languageMenu = new ToolStripMenuItem(Strings.MenuLanguage);

        _languageMenu.DropDownItems.Add(MakeItem(Strings.PlainText, Keys.None,
            () => form.SetLanguage("plaintext")));

        _languageMenu.DropDownItems.Add(new ToolStripSeparator());

        // Custom highlighting submenu (populated dynamically).
        var customMenu = new ToolStripMenuItem(Strings.MenuCustomHighlighting);
        customMenu.DropDownOpening += (_, _) => PopulateCustomHighlightMenu(customMenu, form);
        // Seed with a placeholder so the submenu arrow appears.
        customMenu.DropDownItems.Add(new ToolStripMenuItem("(loading)") { Enabled = false });
        _languageMenu.DropDownItems.Add(customMenu);

        _languageMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (string langId in LexerRegistry.Instance.LanguageIds)
        {
            string captured = langId;
            _languageMenu.DropDownItems.Add(MakeItem(FormatLanguageName(captured), Keys.None,
                () => form.SetLanguage(captured)));
        }

        return _languageMenu;
    }

    private static void PopulateCustomHighlightMenu(ToolStripMenuItem customMenu, MainForm form)
    {
        customMenu.DropDownItems.Clear();

        var profiles = form.CustomHighlightProfiles;
        string? activeName = form.ActiveTab?.Editor.CustomProfileName;

        foreach (var profile in profiles)
        {
            string capturedName = profile.Name;
            var item = new ToolStripMenuItem(capturedName)
            {
                Checked = string.Equals(capturedName, activeName, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => form.SetCustomHighlightProfile(capturedName);
            customMenu.DropDownItems.Add(item);
        }

        if (profiles.Count > 0)
            customMenu.DropDownItems.Add(new ToolStripSeparator());

        var manageItem = new ToolStripMenuItem(Strings.MenuManageCustomHighlighting);
        manageItem.Click += (_, _) => form.ShowCustomHighlightManager();
        customMenu.DropDownItems.Add(manageItem);
    }

    // ── Tools Menu ───────────────────────────────────────────────────

    private ToolStripMenuItem BuildToolsMenu(MainForm form)
    {
        var menu = new ToolStripMenuItem(Strings.MenuTools);

        _hexEditorItem = MakeItem(Strings.MenuHexEditor, Keys.None,
            () => form.ToggleHexEditor());
        menu.DropDownItems.Add(_hexEditorItem);

        menu.DropDownItems.Add(new ToolStripSeparator());

        _recordMacroItem = MakeItem(Strings.MenuRecordMacro, Keys.None,
            () => form.StartMacroRecording());
        menu.DropDownItems.Add(_recordMacroItem);

        _stopRecordingItem = MakeItem(Strings.MenuStopRecording, Keys.None,
            () => form.StopMacroRecording());
        _stopRecordingItem.Enabled = false;
        menu.DropDownItems.Add(_stopRecordingItem);

        _playMacroItem = MakeItem(Strings.MenuPlayMacro, Keys.F5,
            () => form.PlayMacro());
        menu.DropDownItems.Add(_playMacroItem);

        _macroManagerItem = MakeItem(Strings.MenuMacroManager, Keys.None,
            () => form.ShowMacroManager());
        menu.DropDownItems.Add(_macroManagerItem);

        menu.DropDownItems.Add(new ToolStripSeparator());

        menu.DropDownItems.Add(MakeItem(Strings.MenuCompareFiles, Keys.None,
            () => form.CompareFiles()));

        menu.DropDownItems.Add(MakeItem(Strings.MenuSedTransform, Keys.None,
            () => form.SedTransform()));

        menu.DropDownItems.Add(new ToolStripSeparator());

        menu.DropDownItems.Add(MakeItem(Strings.MenuSettings, Keys.None,
            () => form.ShowSettings()));

        return menu;
    }

    /// <summary>
    /// Updates the enabled state of macro-related menu items based on recording state.
    /// </summary>
    public void UpdateMacroMenuState(bool isRecording)
    {
        if (_recordMacroItem is not null) _recordMacroItem.Enabled = !isRecording;
        if (_stopRecordingItem is not null) _stopRecordingItem.Enabled = isRecording;
        if (_playMacroItem is not null) _playMacroItem.Enabled = !isRecording;
        if (_macroManagerItem is not null) _macroManagerItem.Enabled = !isRecording;
    }

    // ── Plugins Menu ─────────────────────────────────────────────────

    private ToolStripMenuItem BuildPluginsMenu(MainForm form)
    {
        _pluginsMenu = new ToolStripMenuItem(Strings.MenuPlugins);

        _pluginsMenu.DropDownItems.Add(MakeItem(Strings.MenuPluginManager, Keys.None,
            () => { })); // Placeholder

        _pluginsMenu.DropDownItems.Add(new ToolStripSeparator());

        return _pluginsMenu;
    }

    // ── Help Menu ────────────────────────────────────────────────────

    private static ToolStripMenuItem BuildHelpMenu(MainForm form)
    {
        var menu = new ToolStripMenuItem(Strings.MenuHelp);

        menu.DropDownItems.Add(MakeItem(Strings.MenuAbout, Keys.None,
            () => form.ShowAbout()));

        return menu;
    }

    // ── State management ────────────────────────────────────────────

    /// <summary>
    /// Updates the enabled state of menu items based on the current editor state.
    /// </summary>
    public void UpdateMenuState(MainForm form)
    {
        var tab = form.ActiveTab;
        bool hasTab = tab is not null;
        var editor = tab?.Editor;

        // File menu.
        if (_saveItem is not null) _saveItem.Enabled = hasTab && tab!.IsModified;
        if (_saveAsItem is not null) _saveAsItem.Enabled = hasTab;
        if (_saveAllItem is not null) _saveAllItem.Enabled = hasTab;
        if (_printItem is not null) _printItem.Enabled = hasTab;
        if (_printPreviewItem is not null) _printPreviewItem.Enabled = hasTab;

        // Edit menu.
        if (_undoItem is not null) _undoItem.Enabled = hasTab && editor!.History.CanUndo;
        if (_redoItem is not null) _redoItem.Enabled = hasTab && editor!.History.CanRedo;
        if (_cutItem is not null) _cutItem.Enabled = hasTab && editor!.SelectionMgr.HasSelection;
        if (_copyItem is not null) _copyItem.Enabled = hasTab && editor!.SelectionMgr.HasSelection;
        if (_deleteItem is not null) _deleteItem.Enabled = hasTab && editor!.SelectionMgr.HasSelection;
        if (_pasteItem is not null) _pasteItem.Enabled = hasTab;
        if (_selectAllItem is not null) _selectAllItem.Enabled = hasTab;
        if (_findItem is not null) _findItem.Enabled = hasTab;
        if (_replaceItem is not null) _replaceItem.Enabled = hasTab;
        if (_findInFilesItem is not null) _findInFilesItem.Enabled = hasTab;
        if (_goToLineItem is not null) _goToLineItem.Enabled = hasTab;

        // Text menu.
        if (_textMenu is not null) _textMenu.Enabled = hasTab;
        bool hasSelection = hasTab && editor!.SelectionMgr.HasSelection;
        foreach (var item in _textSelectionItems)
            item.Enabled = hasSelection;

        // View menu checkmarks.
        if (_wordWrapItem is not null) _wordWrapItem.Checked = hasTab && editor!.WordWrap;
        if (_showWhitespaceItem is not null) _showWhitespaceItem.Checked = hasTab && editor!.ShowWhitespace;
        if (_lineNumbersItem is not null) _lineNumbersItem.Checked = !hasTab || editor!.ShowLineNumbers;

        // Find results panel checkmark.
        if (_findResultsItem is not null)
            _findResultsItem.Checked = form.IsBottomPanelVisible;

        // Tools menu.
        if (_hexEditorItem is not null)
        {
            _hexEditorItem.Enabled = hasTab;
            _hexEditorItem.Checked = hasTab && tab!.Editor.IsHexPanelVisible;
        }
    }

    /// <summary>
    /// Shows or hides the Plugins top-level menu item.
    /// </summary>
    public void SetPluginsMenuVisible(bool visible)
    {
        if (_pluginsMenuItem is not null)
            _pluginsMenuItem.Visible = visible;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="ToolStripMenuItem"/> with text, shortcut, and click handler.
    /// </summary>
    private static ToolStripMenuItem MakeItem(string text, Keys shortcut, Action onClick)
    {
        var item = new ToolStripMenuItem(text);

        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
            item.ShowShortcutKeys = true;
        }

        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>
    /// Converts a language ID (e.g. "csharp") to a display name (e.g. "C#").
    /// </summary>
    private static string FormatLanguageName(string languageId)
    {
        return languageId.ToLowerInvariant() switch
        {
            "csharp" => "C#",
            "javascript" => "JavaScript",
            "typescript" => "TypeScript",
            "python" => "Python",
            "html" => "HTML",
            "css" => "CSS",
            "xml" => "XML",
            "json" => "JSON",
            "sql" => "SQL",
            "bash" => "Bash / Shell",
            "c" => "C",
            "cpp" => "C++",
            "java" => "Java",
            "php" => "PHP",
            "ruby" => "Ruby",
            "go" => "Go",
            "rust" => "Rust",
            "markdown" => "Markdown",
            _ => languageId,
        };
    }

    /// <summary>
    /// Reverse of <see cref="FormatLanguageName"/>: maps a display name back to a language ID.
    /// </summary>
    private static string ResolveLanguageId(string displayName)
    {
        return displayName switch
        {
            "C#" => "csharp",
            "JavaScript" => "javascript",
            "TypeScript" => "typescript",
            "Python" => "python",
            "HTML" => "html",
            "CSS" => "css",
            "XML" => "xml",
            "JSON" => "json",
            "SQL" => "sql",
            "Bash / Shell" => "bash",
            "C" => "c",
            "C++" => "cpp",
            "Java" => "java",
            "PHP" => "php",
            "Ruby" => "ruby",
            "Go" => "go",
            "Rust" => "rust",
            "Markdown" => "markdown",
            _ => displayName.ToLowerInvariant(),
        };
    }
}
