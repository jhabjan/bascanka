namespace Bascanka.App;

internal static class Strings
{
    // Application
    internal static string AppTitle => LocalizationManager.Get("AppTitle");
    internal static string UntitledDocument => LocalizationManager.Get("UntitledDocument");
    internal static string PlainText => LocalizationManager.Get("PlainText");

    // File Menu
    internal static string MenuFile => LocalizationManager.Get("MenuFile");
    internal static string MenuNew => LocalizationManager.Get("MenuNew");
    internal static string MenuOpen => LocalizationManager.Get("MenuOpen");
    internal static string MenuOpenRecent => LocalizationManager.Get("MenuOpenRecent");
    internal static string MenuSave => LocalizationManager.Get("MenuSave");
    internal static string MenuSaveAs => LocalizationManager.Get("MenuSaveAs");
    internal static string MenuSaveAll => LocalizationManager.Get("MenuSaveAll");
    internal static string MenuPrint => LocalizationManager.Get("MenuPrint");
    internal static string MenuPrintPreview => LocalizationManager.Get("MenuPrintPreview");
    internal static string MenuExit => LocalizationManager.Get("MenuExit");

    // Edit Menu
    internal static string MenuEdit => LocalizationManager.Get("MenuEdit");
    internal static string MenuUndo => LocalizationManager.Get("MenuUndo");
    internal static string MenuRedo => LocalizationManager.Get("MenuRedo");
    internal static string MenuCut => LocalizationManager.Get("MenuCut");
    internal static string MenuCopy => LocalizationManager.Get("MenuCopy");
    internal static string MenuPaste => LocalizationManager.Get("MenuPaste");
    internal static string MenuDelete => LocalizationManager.Get("MenuDelete");
    internal static string MenuSelectAll => LocalizationManager.Get("MenuSelectAll");
    internal static string MenuFind => LocalizationManager.Get("MenuFind");
    internal static string MenuReplace => LocalizationManager.Get("MenuReplace");
    internal static string MenuFindInFiles => LocalizationManager.Get("MenuFindInFiles");
    internal static string MenuGoToLine => LocalizationManager.Get("MenuGoToLine");

    // Text Menu
    internal static string MenuText => LocalizationManager.Get("MenuText");
    internal static string MenuCaseConversion => LocalizationManager.Get("MenuCaseConversion");
    internal static string MenuUpperCase => LocalizationManager.Get("MenuUpperCase");
    internal static string MenuLowerCase => LocalizationManager.Get("MenuLowerCase");
    internal static string MenuTitleCase => LocalizationManager.Get("MenuTitleCase");
    internal static string MenuSwapCase => LocalizationManager.Get("MenuSwapCase");
    internal static string MenuTextEncoding => LocalizationManager.Get("MenuTextEncoding");
    internal static string MenuBase64Encode => LocalizationManager.Get("MenuBase64Encode");
    internal static string MenuBase64Decode => LocalizationManager.Get("MenuBase64Decode");
    internal static string MenuUrlEncode => LocalizationManager.Get("MenuUrlEncode");
    internal static string MenuUrlDecode => LocalizationManager.Get("MenuUrlDecode");
    internal static string MenuHtmlEncode => LocalizationManager.Get("MenuHtmlEncode");
    internal static string MenuHtmlDecode => LocalizationManager.Get("MenuHtmlDecode");
    internal static string MenuSortLinesAsc => LocalizationManager.Get("MenuSortLinesAsc");
    internal static string MenuSortLinesDesc => LocalizationManager.Get("MenuSortLinesDesc");
    internal static string MenuRemoveDuplicateLines => LocalizationManager.Get("MenuRemoveDuplicateLines");
    internal static string MenuReverseLines => LocalizationManager.Get("MenuReverseLines");
    internal static string MenuTrimTrailingWhitespace => LocalizationManager.Get("MenuTrimTrailingWhitespace");
    internal static string MenuTrimLeadingWhitespace => LocalizationManager.Get("MenuTrimLeadingWhitespace");
    internal static string MenuCompactWhitespace => LocalizationManager.Get("MenuCompactWhitespace");
    internal static string MenuTabsToSpaces => LocalizationManager.Get("MenuTabsToSpaces");
    internal static string MenuSpacesToTabs => LocalizationManager.Get("MenuSpacesToTabs");
    internal static string MenuReverseText => LocalizationManager.Get("MenuReverseText");

    // JSON
    internal static string MenuJson => LocalizationManager.Get("MenuJson");
    internal static string MenuJsonFormat => LocalizationManager.Get("MenuJsonFormat");
    internal static string MenuJsonMinimize => LocalizationManager.Get("MenuJsonMinimize");

    // View Menu
    internal static string MenuView => LocalizationManager.Get("MenuView");
    internal static string MenuTheme => LocalizationManager.Get("MenuTheme");
    internal static string MenuLanguage => LocalizationManager.Get("MenuLanguage");
    internal static string MenuUILanguage => LocalizationManager.Get("MenuUILanguage");
    internal static string MenuWordWrap => LocalizationManager.Get("MenuWordWrap");
    internal static string MenuShowWhitespace => LocalizationManager.Get("MenuShowWhitespace");
    internal static string MenuLineNumbers => LocalizationManager.Get("MenuLineNumbers");
    internal static string MenuZoomIn => LocalizationManager.Get("MenuZoomIn");
    internal static string MenuZoomOut => LocalizationManager.Get("MenuZoomOut");
    internal static string MenuResetZoom => LocalizationManager.Get("MenuResetZoom");
    internal static string MenuFullScreen => LocalizationManager.Get("MenuFullScreen");
    internal static string MenuSymbolList => LocalizationManager.Get("MenuSymbolList");
    internal static string MenuFindResults => LocalizationManager.Get("MenuFindResults");
    internal static string MenuTerminal => LocalizationManager.Get("MenuTerminal");
    internal static string MenuFolding => LocalizationManager.Get("MenuFolding");
    internal static string MenuToggleFold => LocalizationManager.Get("MenuToggleFold");
    internal static string MenuFoldAll => LocalizationManager.Get("MenuFoldAll");
    internal static string MenuUnfoldAll => LocalizationManager.Get("MenuUnfoldAll");

    // Encoding Menu
    internal static string MenuEncoding => LocalizationManager.Get("MenuEncoding");
    internal static string MenuConvertLineEndings => LocalizationManager.Get("MenuConvertLineEndings");

    // Tools Menu
    internal static string MenuTools => LocalizationManager.Get("MenuTools");
    internal static string MenuHexEditor => LocalizationManager.Get("MenuHexEditor");
    internal static string MenuRecordMacro => LocalizationManager.Get("MenuRecordMacro");
    internal static string MenuStopRecording => LocalizationManager.Get("MenuStopRecording");
    internal static string MenuPlayMacro => LocalizationManager.Get("MenuPlayMacro");
    internal static string MenuMacroManager => LocalizationManager.Get("MenuMacroManager");
    internal static string MenuCompareFiles => LocalizationManager.Get("MenuCompareFiles");
    internal static string CompareWithTab => LocalizationManager.Get("CompareWithTab");
    internal static string CompareWithBrowse => LocalizationManager.Get("CompareWithBrowse");
    internal static string DiffNoDifferences => LocalizationManager.Get("DiffNoDifferences");
    internal static string CompareSelectFirstFile => LocalizationManager.Get("CompareSelectFirstFile");
    internal static string CompareSelectSecondFile => LocalizationManager.Get("CompareSelectSecondFile");
    internal static string MenuSedTransform => LocalizationManager.Get("MenuSedTransform");
    internal static string SedDialogTitle => LocalizationManager.Get("SedDialogTitle");
    internal static string SedExpressionLabel => LocalizationManager.Get("SedExpressionLabel");
    internal static string SedSyntaxHelp => LocalizationManager.Get("SedSyntaxHelp");
    internal static string SedSyntaxPattern => LocalizationManager.Get("SedSyntaxPattern");
    internal static string SedSyntaxPatternDesc => LocalizationManager.Get("SedSyntaxPatternDesc");
    internal static string SedSyntaxReplacement => LocalizationManager.Get("SedSyntaxReplacement");
    internal static string SedSyntaxReplacementDesc => LocalizationManager.Get("SedSyntaxReplacementDesc");
    internal static string SedSyntaxFlags => LocalizationManager.Get("SedSyntaxFlags");
    internal static string SedSyntaxFlagsDesc => LocalizationManager.Get("SedSyntaxFlagsDesc");
    internal static string SedSyntaxDelimiter => LocalizationManager.Get("SedSyntaxDelimiter");
    internal static string SedExamplesHeader => LocalizationManager.Get("SedExamplesHeader");
    internal static string SedExBasic => LocalizationManager.Get("SedExBasic");
    internal static string SedExBasicDesc => LocalizationManager.Get("SedExBasicDesc");
    internal static string SedExFirst => LocalizationManager.Get("SedExFirst");
    internal static string SedExFirstDesc => LocalizationManager.Get("SedExFirstDesc");
    internal static string SedExCaseInsensitive => LocalizationManager.Get("SedExCaseInsensitive");
    internal static string SedExCaseInsensitiveDesc => LocalizationManager.Get("SedExCaseInsensitiveDesc");
    internal static string SedExCustomDelim => LocalizationManager.Get("SedExCustomDelim");
    internal static string SedExCustomDelimDesc => LocalizationManager.Get("SedExCustomDelimDesc");
    internal static string SedExCapture => LocalizationManager.Get("SedExCapture");
    internal static string SedExCaptureDesc => LocalizationManager.Get("SedExCaptureDesc");
    internal static string SedExTrim => LocalizationManager.Get("SedExTrim");
    internal static string SedExTrimDesc => LocalizationManager.Get("SedExTrimDesc");
    internal static string SedExWrap => LocalizationManager.Get("SedExWrap");
    internal static string SedExWrapDesc => LocalizationManager.Get("SedExWrapDesc");
    internal static string SedInvalidExpression => LocalizationManager.Get("SedInvalidExpression");
    internal static string SedPreviewApply => LocalizationManager.Get("SedPreviewApply");
    internal static string SedPreviewDiscard => LocalizationManager.Get("SedPreviewDiscard");
    internal static string SedReplacementCount => LocalizationManager.Get("SedReplacementCount");
    internal static string MenuOpenAppData => LocalizationManager.Get("MenuOpenAppData");
    internal static string MenuSettings => LocalizationManager.Get("MenuSettings");

    // Plugins Menu
    internal static string MenuPlugins => LocalizationManager.Get("MenuPlugins");
    internal static string MenuPluginManager => LocalizationManager.Get("MenuPluginManager");

    // Help Menu
    internal static string MenuHelp => LocalizationManager.Get("MenuHelp");
    internal static string MenuAbout => LocalizationManager.Get("MenuAbout");

    // Status Bar
    internal static string StatusPosition => LocalizationManager.Get("StatusPosition");
    internal static string StatusPositionFormat => LocalizationManager.Get("StatusPositionFormat");
    internal static string StatusSelectionFormat => LocalizationManager.Get("StatusSelectionFormat");

    // Dialogs
    internal static string PromptSaveChanges => LocalizationManager.Get("PromptSaveChanges");
    internal static string ButtonOK => LocalizationManager.Get("ButtonOK");
    internal static string ButtonCancel => LocalizationManager.Get("ButtonCancel");
    internal static string ButtonYes => LocalizationManager.Get("ButtonYes");
    internal static string ButtonNo => LocalizationManager.Get("ButtonNo");

    // Recent Files
    internal static string NoRecentFiles => LocalizationManager.Get("NoRecentFiles");
    internal static string ClearRecentFiles => LocalizationManager.Get("ClearRecentFiles");

    // File Filter
    internal static string FilterAllFiles => LocalizationManager.Get("FilterAllFiles");

    // Errors
    internal static string ErrorFileNotFound => LocalizationManager.Get("ErrorFileNotFound");
    internal static string ErrorOpeningFile => LocalizationManager.Get("ErrorOpeningFile");
    internal static string ErrorSavingFile => LocalizationManager.Get("ErrorSavingFile");
    internal static string FileStillLoading => LocalizationManager.Get("FileStillLoading");
    internal static string ErrorNoDocumentOpen => LocalizationManager.Get("ErrorNoDocumentOpen");
    internal static string ErrorUnhandledException => LocalizationManager.Get("ErrorUnhandledException");
    internal static string ErrorCompilingScript => LocalizationManager.Get("ErrorCompilingScript");

    // File Watcher
    internal static string FileModifiedExternally => LocalizationManager.Get("FileModifiedExternally");
    internal static string FileDeletedExternally => LocalizationManager.Get("FileDeletedExternally");

    // Command Palette
    internal static string CommandPaletteNotYetImplemented => LocalizationManager.Get("CommandPaletteNotYetImplemented");


    // Save progress
    internal static string SavingProgressFormat => LocalizationManager.Get("SavingProgressFormat");
    internal static string ReloadingAfterSave => LocalizationManager.Get("ReloadingAfterSave");
    internal static string ReloadingProgressFormat => LocalizationManager.Get("ReloadingProgressFormat");

    // Zoom
    internal static string ZoomLevelFormat => LocalizationManager.Get("ZoomLevelFormat");

    // Context menu
    internal static string ContextMenuEditWith => LocalizationManager.Get("ContextMenuEditWith");

    // Settings
    internal static string SettingsTitle => LocalizationManager.Get("SettingsTitle");
    internal static string SettingsExplorerContextMenu => LocalizationManager.Get("SettingsExplorerContextMenu");
    internal static string SettingsExplorerContextMenuDesc => LocalizationManager.Get("SettingsExplorerContextMenuDesc");
    internal static string SettingsNewExplorerContextMenu => LocalizationManager.Get("SettingsNewExplorerContextMenu");
    internal static string SettingsNewExplorerContextMenuDesc => LocalizationManager.Get("SettingsNewExplorerContextMenuDesc");
    internal static string SettingsNewExplorerContextMenuError => LocalizationManager.Get("SettingsNewExplorerContextMenuError");
    internal static string SettingsNewExplorerRestartExplorer => LocalizationManager.Get("SettingsNewExplorerRestartExplorer");

    // Settings categories
    internal static string SettingsCategoryEditor => LocalizationManager.Get("SettingsCategoryEditor");
    internal static string SettingsCategoryDisplay => LocalizationManager.Get("SettingsCategoryDisplay");
    internal static string SettingsCategoryPerformance => LocalizationManager.Get("SettingsCategoryPerformance");
    internal static string SettingsCategorySystem => LocalizationManager.Get("SettingsCategorySystem");

    // Settings labels — Editor
    internal static string SettingsFontFamily => LocalizationManager.Get("SettingsFontFamily");
    internal static string SettingsFontSize => LocalizationManager.Get("SettingsFontSize");
    internal static string SettingsTabWidth => LocalizationManager.Get("SettingsTabWidth");
    internal static string SettingsScrollSpeed => LocalizationManager.Get("SettingsScrollSpeed");
    internal static string SettingsScrollSpeedUnit => LocalizationManager.Get("SettingsScrollSpeedUnit");

    // Settings labels — Display
    internal static string SettingsTheme => LocalizationManager.Get("SettingsTheme");
    internal static string SettingsCaretBlinkRate => LocalizationManager.Get("SettingsCaretBlinkRate");
    internal static string SettingsCaretBlinkRateUnit => LocalizationManager.Get("SettingsCaretBlinkRateUnit");
    internal static string SettingsMaxTabWidth => LocalizationManager.Get("SettingsMaxTabWidth");
    internal static string SettingsMaxTabWidthUnit => LocalizationManager.Get("SettingsMaxTabWidthUnit");

    // Settings labels — Performance
    internal static string SettingsLargeFileThreshold => LocalizationManager.Get("SettingsLargeFileThreshold");
    internal static string SettingsLargeFileThresholdUnit => LocalizationManager.Get("SettingsLargeFileThresholdUnit");
    internal static string SettingsFoldingMaxFileSize => LocalizationManager.Get("SettingsFoldingMaxFileSize");
    internal static string SettingsFoldingMaxFileSizeUnit => LocalizationManager.Get("SettingsFoldingMaxFileSizeUnit");
    internal static string SettingsMaxRecentFiles => LocalizationManager.Get("SettingsMaxRecentFiles");
    internal static string SettingsSearchHistoryLimit => LocalizationManager.Get("SettingsSearchHistoryLimit");

    // Settings labels — Editor (continued)
    internal static string SettingsAutoIndent => LocalizationManager.Get("SettingsAutoIndent");
    internal static string SettingsCaretScrollBuffer => LocalizationManager.Get("SettingsCaretScrollBuffer");
    internal static string SettingsCaretScrollBufferUnit => LocalizationManager.Get("SettingsCaretScrollBufferUnit");

    // Settings labels — Display (continued)
    internal static string SettingsTextLeftPadding => LocalizationManager.Get("SettingsTextLeftPadding");
    internal static string SettingsTextLeftPaddingUnit => LocalizationManager.Get("SettingsTextLeftPaddingUnit");
    internal static string SettingsLineSpacing => LocalizationManager.Get("SettingsLineSpacing");
    internal static string SettingsLineSpacingUnit => LocalizationManager.Get("SettingsLineSpacingUnit");
    internal static string SettingsMinZoomFontSize => LocalizationManager.Get("SettingsMinZoomFontSize");
    internal static string SettingsMinZoomFontSizeUnit => LocalizationManager.Get("SettingsMinZoomFontSizeUnit");
    internal static string SettingsWhitespaceOpacity => LocalizationManager.Get("SettingsWhitespaceOpacity");
    internal static string SettingsFoldIndicatorOpacity => LocalizationManager.Get("SettingsFoldIndicatorOpacity");
    internal static string SettingsGutterPaddingLeft => LocalizationManager.Get("SettingsGutterPaddingLeft");
    internal static string SettingsGutterPaddingLeftUnit => LocalizationManager.Get("SettingsGutterPaddingLeftUnit");
    internal static string SettingsGutterPaddingRight => LocalizationManager.Get("SettingsGutterPaddingRight");
    internal static string SettingsGutterPaddingRightUnit => LocalizationManager.Get("SettingsGutterPaddingRightUnit");
    internal static string SettingsFoldButtonSize => LocalizationManager.Get("SettingsFoldButtonSize");
    internal static string SettingsFoldButtonSizeUnit => LocalizationManager.Get("SettingsFoldButtonSizeUnit");
    internal static string SettingsBookmarkSize => LocalizationManager.Get("SettingsBookmarkSize");
    internal static string SettingsBookmarkSizeUnit => LocalizationManager.Get("SettingsBookmarkSizeUnit");
    internal static string SettingsTabHeight => LocalizationManager.Get("SettingsTabHeight");
    internal static string SettingsTabHeightUnit => LocalizationManager.Get("SettingsTabHeightUnit");
    internal static string SettingsMinTabWidth => LocalizationManager.Get("SettingsMinTabWidth");
    internal static string SettingsMinTabWidthUnit => LocalizationManager.Get("SettingsMinTabWidthUnit");
    internal static string SettingsMenuItemPadding => LocalizationManager.Get("SettingsMenuItemPadding");
    internal static string SettingsMenuItemPaddingUnit => LocalizationManager.Get("SettingsMenuItemPaddingUnit");
    internal static string SettingsTerminalPadding => LocalizationManager.Get("SettingsTerminalPadding");
    internal static string SettingsTerminalPaddingUnit => LocalizationManager.Get("SettingsTerminalPaddingUnit");

    // Settings labels — Performance (continued)
    internal static string SettingsSearchDebounce => LocalizationManager.Get("SettingsSearchDebounce");
    internal static string SettingsSearchDebounceUnit => LocalizationManager.Get("SettingsSearchDebounceUnit");
    internal static string SettingsAutoSaveInterval => LocalizationManager.Get("SettingsAutoSaveInterval");
    internal static string SettingsAutoSaveIntervalUnit => LocalizationManager.Get("SettingsAutoSaveIntervalUnit");

    // Settings categories (continued)
    internal static string SettingsCategoryAppearance => LocalizationManager.Get("SettingsCategoryAppearance");

    // Settings descriptions — Editor
    internal static string SettingsFontFamilyDesc => LocalizationManager.Get("SettingsFontFamilyDesc");
    internal static string SettingsFontSizeDesc => LocalizationManager.Get("SettingsFontSizeDesc");
    internal static string SettingsTabWidthDesc => LocalizationManager.Get("SettingsTabWidthDesc");
    internal static string SettingsAutoIndentDesc => LocalizationManager.Get("SettingsAutoIndentDesc");
    internal static string SettingsScrollSpeedDesc => LocalizationManager.Get("SettingsScrollSpeedDesc");
    internal static string SettingsCaretScrollBufferDesc => LocalizationManager.Get("SettingsCaretScrollBufferDesc");

    // Settings descriptions — Appearance
    internal static string SettingsThemeDesc => LocalizationManager.Get("SettingsThemeDesc");
    internal static string SettingsUILanguage => LocalizationManager.Get("SettingsUILanguage");
    internal static string SettingsUILanguageDesc => LocalizationManager.Get("SettingsUILanguageDesc");
    internal static string SettingsRecentFilesSeparated => LocalizationManager.Get("SettingsRecentFilesSeparated");
    internal static string SettingsRecentFilesSeparatedDesc => LocalizationManager.Get("SettingsRecentFilesSeparatedDesc");

    // Settings descriptions — Display
    internal static string SettingsCaretBlinkRateDesc => LocalizationManager.Get("SettingsCaretBlinkRateDesc");
    internal static string SettingsTextLeftPaddingDesc => LocalizationManager.Get("SettingsTextLeftPaddingDesc");
    internal static string SettingsLineSpacingDesc => LocalizationManager.Get("SettingsLineSpacingDesc");
    internal static string SettingsMinZoomFontSizeDesc => LocalizationManager.Get("SettingsMinZoomFontSizeDesc");
    internal static string SettingsWhitespaceOpacityDesc => LocalizationManager.Get("SettingsWhitespaceOpacityDesc");
    internal static string SettingsFoldIndicatorOpacityDesc => LocalizationManager.Get("SettingsFoldIndicatorOpacityDesc");
    internal static string SettingsGutterPaddingLeftDesc => LocalizationManager.Get("SettingsGutterPaddingLeftDesc");
    internal static string SettingsGutterPaddingRightDesc => LocalizationManager.Get("SettingsGutterPaddingRightDesc");
    internal static string SettingsFoldButtonSizeDesc => LocalizationManager.Get("SettingsFoldButtonSizeDesc");
    internal static string SettingsBookmarkSizeDesc => LocalizationManager.Get("SettingsBookmarkSizeDesc");
    internal static string SettingsTabHeightDesc => LocalizationManager.Get("SettingsTabHeightDesc");
    internal static string SettingsMaxTabWidthDesc => LocalizationManager.Get("SettingsMaxTabWidthDesc");
    internal static string SettingsMinTabWidthDesc => LocalizationManager.Get("SettingsMinTabWidthDesc");
    internal static string SettingsMenuItemPaddingDesc => LocalizationManager.Get("SettingsMenuItemPaddingDesc");
    internal static string SettingsTerminalPaddingDesc => LocalizationManager.Get("SettingsTerminalPaddingDesc");

    // Settings descriptions — Performance
    internal static string SettingsLargeFileThresholdDesc => LocalizationManager.Get("SettingsLargeFileThresholdDesc");
    internal static string SettingsFoldingMaxFileSizeDesc => LocalizationManager.Get("SettingsFoldingMaxFileSizeDesc");
    internal static string SettingsMaxRecentFilesDesc => LocalizationManager.Get("SettingsMaxRecentFilesDesc");
    internal static string SettingsSearchHistoryLimitDesc => LocalizationManager.Get("SettingsSearchHistoryLimitDesc");
    internal static string SettingsSearchDebounceDesc => LocalizationManager.Get("SettingsSearchDebounceDesc");
    internal static string SettingsAutoSaveIntervalDesc => LocalizationManager.Get("SettingsAutoSaveIntervalDesc");

    // Color customization
    internal static string SettingsColorCustomization => LocalizationManager.Get("SettingsColorCustomization");
    internal static string SettingsColorReset => LocalizationManager.Get("SettingsColorReset");
    internal static string SettingsColorResetAll => LocalizationManager.Get("SettingsColorResetAll");
    internal static string SettingsColorGroupEditor => LocalizationManager.Get("SettingsColorGroupEditor");
    internal static string SettingsColorGroupGutter => LocalizationManager.Get("SettingsColorGroupGutter");
    internal static string SettingsColorGroupTabs => LocalizationManager.Get("SettingsColorGroupTabs");
    internal static string SettingsColorGroupStatusBar => LocalizationManager.Get("SettingsColorGroupStatusBar");
    internal static string SettingsColorGroupFindPanel => LocalizationManager.Get("SettingsColorGroupFindPanel");
    internal static string SettingsColorGroupMenus => LocalizationManager.Get("SettingsColorGroupMenus");
    internal static string SettingsColorGroupScrollBar => LocalizationManager.Get("SettingsColorGroupScrollBar");
    internal static string SettingsColorGroupDiff => LocalizationManager.Get("SettingsColorGroupDiff");
    internal static string SettingsColorGroupOther => LocalizationManager.Get("SettingsColorGroupOther");

    // Export/Import
    internal static string SettingsExportSettings => LocalizationManager.Get("SettingsExportSettings");
    internal static string SettingsImportSettings => LocalizationManager.Get("SettingsImportSettings");
    internal static string SettingsExportSuccess => LocalizationManager.Get("SettingsExportSuccess");
    internal static string SettingsImportSuccess => LocalizationManager.Get("SettingsImportSuccess");
    internal static string SettingsImportError => LocalizationManager.Get("SettingsImportError");
    internal static string SettingsJsonFilter => LocalizationManager.Get("SettingsJsonFilter");

    // Settings buttons
    internal static string SettingsResetDefaults => LocalizationManager.Get("SettingsResetDefaults");
    internal static string SettingsResetConfirm => LocalizationManager.Get("SettingsResetConfirm");

    // Tab Context Menu
    internal static string TabMenuClose => LocalizationManager.Get("TabMenuClose");
    internal static string TabMenuCloseOthers => LocalizationManager.Get("TabMenuCloseOthers");
    internal static string TabMenuCloseAll => LocalizationManager.Get("TabMenuCloseAll");
    internal static string TabMenuCloseToRight => LocalizationManager.Get("TabMenuCloseToRight");
    internal static string TabMenuCopyPath => LocalizationManager.Get("TabMenuCopyPath");
    internal static string TabMenuOpenInExplorer => LocalizationManager.Get("TabMenuOpenInExplorer");

    // Find Results Context Menu
    internal static string FindResultsCopyLine => LocalizationManager.Get("FindResultsCopyLine");
    internal static string FindResultsCopyAll => LocalizationManager.Get("FindResultsCopyAll");
    internal static string FindResultsCopyPath => LocalizationManager.Get("FindResultsCopyPath");
    internal static string FindResultsOpenInNewTab => LocalizationManager.Get("FindResultsOpenInNewTab");
    internal static string FindResultsRemoveSearch => LocalizationManager.Get("FindResultsRemoveSearch");
    internal static string FindResultsClearAll => LocalizationManager.Get("FindResultsClearAll");
    internal static string FindResultsHeader => LocalizationManager.Get("FindResultsHeader");
    internal static string FindResultsHeaderFormat => LocalizationManager.Get("FindResultsHeaderFormat");

    // Find Results Scope Labels
    internal static string ScopeCurrentDocument => LocalizationManager.Get("ScopeCurrentDocument");
    internal static string ScopeAllOpenTabs => LocalizationManager.Get("ScopeAllOpenTabs");
    internal static string FindResultMatchFormat => LocalizationManager.Get("FindResultMatchFormat");
    internal static string FindResultMatchFilesFormat => LocalizationManager.Get("FindResultMatchFilesFormat");

    // Editor Context Menu
    internal static string CtxUndo => LocalizationManager.Get("CtxUndo");
    internal static string CtxRedo => LocalizationManager.Get("CtxRedo");
    internal static string CtxCut => LocalizationManager.Get("CtxCut");
    internal static string CtxCopy => LocalizationManager.Get("CtxCopy");
    internal static string CtxPaste => LocalizationManager.Get("CtxPaste");
    internal static string CtxDelete => LocalizationManager.Get("CtxDelete");
    internal static string CtxSelectAll => LocalizationManager.Get("CtxSelectAll");

    // Editor Context Menu - Selected Text submenu
    internal static string CtxSelectedText => LocalizationManager.Get("CtxSelectedText");

    // Find/Replace Panel
    internal static string FindPanelMarkAll => LocalizationManager.Get("FindPanelMarkAll");
    internal static string FindPanelFindAll => LocalizationManager.Get("FindPanelFindAll");
    internal static string FindPanelFindInTabs => LocalizationManager.Get("FindPanelFindInTabs");
    internal static string FindPanelReplace => LocalizationManager.Get("FindPanelReplace");
    internal static string FindPanelReplaceAll => LocalizationManager.Get("FindPanelReplaceAll");

    // Admin Elevation
    internal static string ErrorAccessDenied => LocalizationManager.Get("ErrorAccessDenied");
    internal static string RestartAsAdmin => LocalizationManager.Get("RestartAsAdmin");

    // Custom Highlighting
    internal static string MenuCustomHighlighting => LocalizationManager.Get("MenuCustomHighlighting");
    internal static string MenuManageCustomHighlighting => LocalizationManager.Get("MenuManageCustomHighlighting");
    internal static string CustomHighlightTitle => LocalizationManager.Get("CustomHighlightTitle");
    internal static string CustomHighlightAddProfile => LocalizationManager.Get("CustomHighlightAddProfile");
    internal static string CustomHighlightDeleteProfile => LocalizationManager.Get("CustomHighlightDeleteProfile");
    internal static string CustomHighlightAddRule => LocalizationManager.Get("CustomHighlightAddRule");
    internal static string CustomHighlightDeleteRule => LocalizationManager.Get("CustomHighlightDeleteRule");
    internal static string CustomHighlightPattern => LocalizationManager.Get("CustomHighlightPattern");
    internal static string CustomHighlightScope => LocalizationManager.Get("CustomHighlightScope");
    internal static string CustomHighlightForeground => LocalizationManager.Get("CustomHighlightForeground");
    internal static string CustomHighlightBackground => LocalizationManager.Get("CustomHighlightBackground");
    internal static string CustomHighlightScopeLine => LocalizationManager.Get("CustomHighlightScopeLine");
    internal static string CustomHighlightScopeMatch => LocalizationManager.Get("CustomHighlightScopeMatch");
    internal static string CustomHighlightSave => LocalizationManager.Get("CustomHighlightSave");
    internal static string CustomHighlightNewProfile => LocalizationManager.Get("CustomHighlightNewProfile");
    internal static string CustomHighlightEndPattern => LocalizationManager.Get("CustomHighlightEndPattern");
    internal static string CustomHighlightFoldable => LocalizationManager.Get("CustomHighlightFoldable");
    internal static string CustomHighlightScopeBlock => LocalizationManager.Get("CustomHighlightScopeBlock");
    internal static string CustomHighlightExport => LocalizationManager.Get("CustomHighlightExport");
    internal static string CustomHighlightImport => LocalizationManager.Get("CustomHighlightImport");
    internal static string CustomHighlightExportSuccess => LocalizationManager.Get("CustomHighlightExportSuccess");
    internal static string CustomHighlightExportEmpty => LocalizationManager.Get("CustomHighlightExportEmpty");
    internal static string CustomHighlightImportSuccess => LocalizationManager.Get("CustomHighlightImportSuccess");
    internal static string CustomHighlightImportError => LocalizationManager.Get("CustomHighlightImportError");
    internal static string CustomHighlightPickColor => LocalizationManager.Get("CustomHighlightPickColor");
    internal static string CustomHighlightClearColor => LocalizationManager.Get("CustomHighlightClearColor");
}
