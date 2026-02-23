using Bascanka.Core.Encoding;
using Bascanka.Core.Syntax;
using Bascanka.Editor.Tabs;

namespace Bascanka.App;

/// <summary>
/// Manages the status bar at the bottom of the main window.
/// Displays cursor position, selection, encoding, line ending, language,
/// file size, insert/overwrite mode, and read-only indicator.
/// </summary>
public sealed class StatusBarManager
{
    private readonly StatusStrip _statusStrip;

    // Status bar fields.
    private readonly ToolStripStatusLabel _positionLabel;
    private readonly ToolStripStatusLabel _selectionLabel;
    private readonly ToolStripStatusLabel _encodingLabel;
    private readonly ToolStripStatusLabel _lineEndingLabel;
    private readonly ToolStripStatusLabel _languageLabel;
    private readonly ToolStripStatusLabel _fileSizeLabel;
    private readonly ToolStripStatusLabel _insertModeLabel;
    private readonly ToolStripStatusLabel _readOnlyLabel;
    private readonly ToolStripStatusLabel _macroRecordingLabel;
    private readonly ToolStripStatusLabel _zoomLabel;
    private readonly ToolStripStatusLabel _brandingLabel;
    private readonly ToolStripStatusLabel _springLabel;

    // Base widths used as proportional weights; actual widths are scaled
    // to the status strip's current width on every resize.
    private const int ReferenceWidth = 1000;
    private readonly (ToolStripStatusLabel Label, int BaseWidth)[] _scaledLabels;

    public StatusBarManager(StatusStrip statusStrip)
    {
        _statusStrip = statusStrip;
        _statusStrip.SizingGrip = true;
        _statusStrip.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;

        // Spring label pushes subsequent items to the right.
        _springLabel = new ToolStripStatusLabel
        {
            Spring = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        };

        _positionLabel = CreateLabel(Strings.StatusPosition, 120);
        _selectionLabel = CreateLabel(string.Empty, 80);
        _encodingLabel = CreateClickableLabel("UTF-8", 90);
        _lineEndingLabel = CreateClickableLabel("CRLF", 50);
        _languageLabel = CreateClickableLabel(Strings.PlainText, 100);
        _fileSizeLabel = CreateLabel("0 B", 130);
        _insertModeLabel = CreateLabel("INS", 40);
        _readOnlyLabel = CreateLabel(string.Empty, 30);
        _zoomLabel = CreateLabel(string.Format(Strings.ZoomLevelFormat, 100), 90);

        string version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "1.0.0";
#if DEBUG
        string brandingText = $"Bascanka v.{version} \u00a9 jhabjan - DEBUG";
#else
        string brandingText = $"Bascanka v.{version} \u00a9 jhabjan";
#endif
        _brandingLabel = new ToolStripStatusLabel(brandingText)
        {
            Alignment = ToolStripItemAlignment.Right,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            BorderSides = ToolStripStatusLabelBorderSides.None,
            Padding = new System.Windows.Forms.Padding(4, 0, 4, 0),
        };

        _macroRecordingLabel = new ToolStripStatusLabel("REC")
        {
            AutoSize = false,
            Width = 40,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            ForeColor = System.Drawing.Color.White,
            BackColor = System.Drawing.Color.FromArgb(200, 40, 40),
            Font = new System.Drawing.Font(_statusStrip.Font.FontFamily, _statusStrip.Font.Size, System.Drawing.FontStyle.Bold),
            Visible = false,
        };

        _scaledLabels =
        [
            (_positionLabel,  120),
            (_selectionLabel,  80),
            (_encodingLabel,   90),
            (_lineEndingLabel, 50),
            (_languageLabel,  100),
            (_fileSizeLabel,  130),
            (_insertModeLabel, 40),
            (_readOnlyLabel,   30),
            (_zoomLabel,       90),
            (_macroRecordingLabel, 40),
        ];

        _statusStrip.Items.AddRange(new ToolStripItem[]
        {
            _positionLabel,
            _selectionLabel,
            _macroRecordingLabel,
            _springLabel,
            _fileSizeLabel,
            _encodingLabel,
            _lineEndingLabel,
            _languageLabel,
            _zoomLabel,
            _insertModeLabel,
            _readOnlyLabel,
            _brandingLabel,
        });

        _statusStrip.Resize += (_, _) => ScaleLabelWidths();
    }

    /// <summary>
    /// Updates all status bar fields based on the given tab's editor state.
    /// </summary>
    public void Update(TabInfo tab)
    {
        var editor = tab.Editor;

        // Position.
        _positionLabel.Text = string.Format(
            Strings.StatusPositionFormat,
            editor.CurrentLine,
            editor.CurrentColumn);

        // Selection.
        int sel = editor.SelectionLength;
        _selectionLabel.Text = sel > 0
            ? string.Format(Strings.StatusSelectionFormat, sel)
            : string.Empty;

        // Encoding.
        EncodingManager? enc = editor.EncodingManager;
        if (enc is not null)
        {
            string encodingName = GetEncodingDisplayName(enc);
            _encodingLabel.Text = encodingName;
        }
        else
        {
            _encodingLabel.Text = "UTF-8";
        }

        // Line ending.
        _lineEndingLabel.Text = editor.LineEnding;

        // Language / custom profile.
        if (editor.CustomProfileName is not null)
        {
            _languageLabel.Text = editor.CustomProfileName;
        }
        else
        {
            ILexer? lexer = editor.CurrentLexer;
            _languageLabel.Text = lexer is not null
                ? FormatLanguageName(lexer.LanguageId)
                : Strings.PlainText;
        }

        // File size (bytes on disk).
        long length = editor.FileSizeBytes;
        _fileSizeLabel.Text = FormatFileSize(length);

        // Zoom level.
        _zoomLabel.Text = string.Format(Strings.ZoomLevelFormat, editor.ZoomPercentage);

        // Insert / Overwrite mode.
        _insertModeLabel.Text = editor.InsertMode ? "INS" : "OVR";

        // Read-only indicator.
        _readOnlyLabel.Text = editor.IsReadOnly ? "R/O" : string.Empty;
    }

    /// <summary>
    /// Shows a loading progress indicator in the status bar.
    /// Overrides position and file size labels with progress info.
    /// </summary>
    public void ShowLoadingProgress(long scannedBytes, long totalBytes)
    {
        int percent = totalBytes > 0 ? (int)(scannedBytes * 100 / totalBytes) : 0;
        _positionLabel.Text = $"Loading {percent}%...";
        _fileSizeLabel.Text = $"{FormatFileSize(scannedBytes)} / {FormatFileSize(totalBytes)}";
        _readOnlyLabel.Text = string.Empty; // suppress "R/O" — the loading label is enough
    }

    /// <summary>
    /// Shows or hides the macro recording indicator in the status bar.
    /// </summary>
    public void SetMacroRecording(bool isRecording)
    {
        _macroRecordingLabel.Visible = isRecording;
    }

    /// <summary>
    /// Refreshes static label text after a UI language change.
    /// </summary>
    public void RefreshLabels()
    {
        _positionLabel.Text = Strings.StatusPosition;
    }

    /// <summary>
    /// Clears all status bar fields (no document open).
    /// </summary>
    public void Clear()
    {
        _positionLabel.Text = string.Empty;
        _selectionLabel.Text = string.Empty;
        _encodingLabel.Text = string.Empty;
        _lineEndingLabel.Text = string.Empty;
        _languageLabel.Text = string.Empty;
        _fileSizeLabel.Text = string.Empty;
        _zoomLabel.Text = string.Empty;
        _insertModeLabel.Text = string.Empty;
        _readOnlyLabel.Text = string.Empty;
    }

    /// <summary>
    /// Sets or updates a plugin-owned custom field in the status bar.
    /// </summary>
    public void SetPluginField(string id, string text)
    {
        string name = $"plugin_{id}";
        ToolStripItem? existing = null;
        foreach (ToolStripItem item in _statusStrip.Items)
        {
            if (item.Name == name)
            {
                existing = item;
                break;
            }
        }

        if (existing is ToolStripStatusLabel label)
        {
            label.Text = text;
        }
        else
        {
            var newLabel = CreateLabel(text, 80);
            newLabel.Name = name;
            // Insert before the spring.
            int springIndex = _statusStrip.Items.IndexOf(_springLabel);
            _statusStrip.Items.Insert(springIndex, newLabel);
        }
    }

    /// <summary>
    /// Removes a plugin-owned custom field from the status bar.
    /// </summary>
    public void RemovePluginField(string id)
    {
        string name = $"plugin_{id}";
        for (int i = _statusStrip.Items.Count - 1; i >= 0; i--)
        {
            if (_statusStrip.Items[i].Name == name)
            {
                _statusStrip.Items.RemoveAt(i);
                break;
            }
        }
    }

    // ── Proportional sizing ────────────────────────────────────────

    private void ScaleLabelWidths()
    {
        double scale = _statusStrip.Width / (double)ReferenceWidth;
        foreach (var (label, baseWidth) in _scaledLabels)
        {
            label.Width = Math.Max(20, (int)(baseWidth * scale));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static ToolStripStatusLabel CreateLabel(string text, int width)
    {
        return new ToolStripStatusLabel(text)
        {
            AutoSize = false,
            Width = width,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            BorderSides = ToolStripStatusLabelBorderSides.Right,
            BorderStyle = Border3DStyle.Etched,
        };
    }

    private ToolStripStatusLabel CreateClickableLabel(string text, int width)
    {
        var label = CreateLabel(text, width);
        label.IsLink = false;
        label.Click += OnStatusLabelClick;
        return label;
    }

    private void OnStatusLabelClick(object? sender, EventArgs e)
    {
        if (sender == _encodingLabel)
            ShowEncodingPopup();
        else if (sender == _lineEndingLabel)
            ShowLineEndingPopup();
        else if (sender == _languageLabel)
            ShowLanguagePopup();
    }

    private void ShowEncodingPopup()
    {
        var form = _statusStrip.FindForm() as MainForm;
        if (form is null) return;

        string currentEncoding = _encodingLabel.Text ?? "";

        var menu = new ContextMenuStrip();
        menu.Items.Add(MakeCheckedPopupItem("UTF-8", currentEncoding, () =>
            form.SetEncoding(new System.Text.UTF8Encoding(false), false)));
        menu.Items.Add(MakeCheckedPopupItem("UTF-8 with BOM", currentEncoding, () =>
            form.SetEncoding(new System.Text.UTF8Encoding(true), true)));
        menu.Items.Add(MakeCheckedPopupItem("UTF-16 LE", currentEncoding, () =>
            form.SetEncoding(System.Text.Encoding.Unicode, true)));
        menu.Items.Add(MakeCheckedPopupItem("UTF-16 BE", currentEncoding, () =>
            form.SetEncoding(System.Text.Encoding.BigEndianUnicode, true)));
        menu.Items.Add(MakeCheckedPopupItem("ASCII", currentEncoding, () =>
            form.SetEncoding(System.Text.Encoding.ASCII, false)));
        menu.Items.Add(MakeCheckedPopupItem("Windows-1252", currentEncoding, () =>
            form.SetEncoding(System.Text.Encoding.GetEncoding(1252), false)));
        menu.Items.Add(MakeCheckedPopupItem("ISO-8859-1", currentEncoding, () =>
            form.SetEncoding(System.Text.Encoding.GetEncoding("iso-8859-1"), false)));
        menu.Items.Add(MakeCheckedPopupItem(Strings.MenuEncodingChineseGB18030, currentEncoding, () =>
            form.SetEncoding(System.Text.Encoding.GetEncoding("GB18030"), false)));

        ShowPopupAboveLabel(_encodingLabel, menu);
    }

    private void ShowLineEndingPopup()
    {
        var form = _statusStrip.FindForm() as MainForm;
        if (form is null) return;

        string currentLineEnding = _lineEndingLabel.Text ?? "";

        var menu = new ContextMenuStrip();
        menu.Items.Add(MakeCheckedPopupItem("CRLF (Windows)", currentLineEnding,
            () => form.SetLineEnding("CRLF"), matchPrefix: true));
        menu.Items.Add(MakeCheckedPopupItem("LF (Unix/macOS)", currentLineEnding,
            () => form.SetLineEnding("LF"), matchPrefix: true));
        menu.Items.Add(MakeCheckedPopupItem("CR (Classic Mac)", currentLineEnding,
            () => form.SetLineEnding("CR"), matchPrefix: true));

        ShowPopupAboveLabel(_lineEndingLabel, menu);
    }

    private void ShowLanguagePopup()
    {
        var form = _statusStrip.FindForm() as MainForm;
        if (form is null) return;

        string currentLanguage = _languageLabel.Text ?? "";

        var menu = new ContextMenuStrip();
        menu.Items.Add(MakeCheckedPopupItem(Strings.PlainText, currentLanguage,
            () => form.SetLanguage("plaintext")));

        // Custom highlighting profiles.
        var customProfiles = form.CustomHighlightProfiles;
        if (customProfiles.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            foreach (var profile in customProfiles)
            {
                string capturedName = profile.Name;
                menu.Items.Add(MakeCheckedPopupItem(capturedName, currentLanguage,
                    () => form.SetCustomHighlightProfile(capturedName)));
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        foreach (string langId in LexerRegistry.Instance.LanguageIds)
        {
            string captured = langId;
            string displayName = FormatLanguageName(captured);
            menu.Items.Add(MakeCheckedPopupItem(displayName, currentLanguage,
                () => form.SetLanguage(captured)));
        }

        ShowPopupAboveLabel(_languageLabel, menu);
    }

    private void ShowPopupAboveLabel(ToolStripStatusLabel label, ContextMenuStrip menu)
    {
        var bounds = label.Bounds;
        var screenPoint = _statusStrip.PointToScreen(
            new System.Drawing.Point(bounds.Left, bounds.Top - menu.Height));

        // Ensure the menu appears above the status bar.
        menu.Show(screenPoint);
    }

    private static ToolStripMenuItem MakeCheckedPopupItem(
        string text, string currentValue, Action onClick, bool matchPrefix = false)
    {
        var item = new ToolStripMenuItem(text);
        item.Checked = matchPrefix
            ? text.StartsWith(currentValue, StringComparison.OrdinalIgnoreCase)
            : string.Equals(text, currentValue, StringComparison.OrdinalIgnoreCase);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static string GetEncodingDisplayName(EncodingManager enc)
    {
        string name = enc.CurrentEncoding.WebName.ToUpperInvariant();

        if (name == "UTF-8" && enc.HasBom)
            return "UTF-8 BOM";
        if (name == "UTF-16" || name == "UTF-16LE")
            return "UTF-16 LE";
        if (name == "UTF-16BE")
            return "UTF-16 BE";

        return name switch
        {
            "GB2312" => Strings.MenuEncodingChineseGB18030,
            "GB18030" => Strings.MenuEncodingChineseGB18030,
            _ => name,
        };
    }

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
            "bash" => "Bash",
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

    internal static string FormatFileSize(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        return bytes switch
        {
            >= GB => $"{bytes / (double)GB:F1} GB",
            >= MB => $"{bytes / (double)MB:F1} MB",
            >= KB => $"{bytes / (double)KB:F1} KB",
            _ => $"{bytes} B",
        };
    }
}
