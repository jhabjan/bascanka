using System.Drawing;
using System.Runtime.InteropServices;
using Bascanka.Core.Search;
using Bascanka.Core.Syntax;
using Bascanka.Editor.Themes;

namespace Bascanka.App;

/// <summary>
/// A modal dialog for entering a sed substitution expression.
/// Validates the expression before allowing OK. The expression
/// input uses a <see cref="RichTextBox"/> with real-time syntax
/// colorization of the sed parts (command, delimiters, pattern,
/// replacement, flags).
/// </summary>
internal sealed class SedInputDialog : Form
{
    private readonly RichTextBox _expressionBox;
    private readonly Label _exprLabel;
    private readonly Label _syntaxLabel;
    private readonly Label _delimiterHint;
    private readonly Label _examplesHeader;
    private readonly ListView _examplesList;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    private ITheme? _theme;

    /// <summary>The sed expression entered by the user.</summary>
    public string Expression => _expressionBox.Text;

    public SedInputDialog()
    {
        Text = Strings.SedDialogTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 480);
        ShowInTaskbar = false;

        int leftMargin = 20;
        int rightEdge = 520;
        int y = 16;

        // ── Expression input ────────────────────────────────────────

        _exprLabel = new Label
        {
            Text = Strings.SedExpressionLabel,
            AutoSize = true,
            Top = y + 3,
            Left = leftMargin,
        };

        _expressionBox = new RichTextBox
        {
            Top = y,
            Left = leftMargin + 80,
            Width = rightEdge - leftMargin - 80,
            Height = 28,
            Font = new Font("Consolas", 11f),
            Multiline = true,
            ScrollBars = RichTextBoxScrollBars.None,
            AcceptsTab = false,
            WordWrap = false,
            BorderStyle = BorderStyle.FixedSingle,
            DetectUrls = false,
        };
        _expressionBox.TextChanged += (_, _) => ColorizeSedExpression();
        _expressionBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OnOkClick(this, EventArgs.Empty);
            }
        };

        y += 40;

        // ── Syntax help ─────────────────────────────────────────────

        _syntaxLabel = new Label
        {
            Text = Strings.SedSyntaxHelp + "\n\n" +
                   $"  {Strings.SedSyntaxPattern,-16}{Strings.SedSyntaxPatternDesc}\n" +
                   $"  {Strings.SedSyntaxReplacement,-16}{Strings.SedSyntaxReplacementDesc}\n" +
                   $"  {Strings.SedSyntaxFlags,-16}{Strings.SedSyntaxFlagsDesc}",
            Top = y,
            Left = leftMargin,
            Width = rightEdge - leftMargin,
            Height = 80,
            Font = new Font("Consolas", 9f),
        };

        y += 84;

        _delimiterHint = new Label
        {
            Text = Strings.SedSyntaxDelimiter,
            Top = y,
            Left = leftMargin,
            Width = rightEdge - leftMargin,
            AutoSize = false,
            Height = 18,
            Font = new Font("Segoe UI", 8f, FontStyle.Italic),
        };

        y += 26;

        // ── Separator ───────────────────────────────────────────────

        var separator = new Label
        {
            Top = y,
            Left = leftMargin,
            Width = rightEdge - leftMargin,
            Height = 1,
            BorderStyle = BorderStyle.Fixed3D,
        };

        y += 10;

        // ── Examples list ───────────────────────────────────────────

        _examplesHeader = new Label
        {
            Text = Strings.SedExamplesHeader,
            AutoSize = true,
            Top = y,
            Left = leftMargin,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };

        y += 22;

        _examplesList = new ListView
        {
            Top = y,
            Left = leftMargin,
            Width = rightEdge - leftMargin,
            Height = 170,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            MultiSelect = false,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
            GridLines = false,
        };
        _examplesList.Columns.Add("Expression", 240);
        _examplesList.Columns.Add("Description", 260);

        AddExample(Strings.SedExBasic, Strings.SedExBasicDesc);
        AddExample(Strings.SedExFirst, Strings.SedExFirstDesc);
        AddExample(Strings.SedExCaseInsensitive, Strings.SedExCaseInsensitiveDesc);
        AddExample(Strings.SedExCustomDelim, Strings.SedExCustomDelimDesc);
        AddExample(Strings.SedExCapture, Strings.SedExCaptureDesc);
        AddExample(Strings.SedExTrim, Strings.SedExTrimDesc);
        AddExample(Strings.SedExWrap, Strings.SedExWrapDesc);

        // Double-click an example to populate the expression box.
        _examplesList.DoubleClick += (_, _) =>
        {
            if (_examplesList.SelectedItems.Count > 0)
            {
                _expressionBox.Text = _examplesList.SelectedItems[0].Text;
                _expressionBox.Focus();
                _expressionBox.SelectionStart = _expressionBox.Text.Length;
            }
        };

        y += 175;

        // ── OK / Cancel buttons ─────────────────────────────────────

        _okButton = new Button
        {
            Text = Strings.ButtonOK,
            DialogResult = DialogResult.None,
            Width = 80,
            Height = 30,
            Top = y,
            Left = rightEdge - 170,
        };
        _okButton.Click += OnOkClick;

        _cancelButton = new Button
        {
            Text = Strings.ButtonCancel,
            DialogResult = DialogResult.Cancel,
            Width = 80,
            Height = 30,
            Top = y,
            Left = rightEdge - 80,
        };

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Controls.AddRange([
            _exprLabel, _expressionBox,
            _syntaxLabel, _delimiterHint,
            separator,
            _examplesHeader, _examplesList,
            _okButton, _cancelButton,
        ]);
    }

    private void AddExample(string expression, string description)
    {
        var item = new ListViewItem(expression);
        item.SubItems.Add(description);
        _examplesList.Items.Add(item);
    }

    /// <summary>
    /// Applies theme colors to the dialog and the expression input.
    /// </summary>
    public void ApplyTheme(ITheme theme)
    {
        _theme = theme;

        BackColor = theme.EditorBackground;
        ForeColor = theme.EditorForeground;

        Color dimColor = Color.FromArgb(
            theme.EditorForeground.A,
            Math.Min(255, (theme.EditorForeground.R + theme.EditorBackground.R) / 2),
            Math.Min(255, (theme.EditorForeground.G + theme.EditorBackground.G) / 2),
            Math.Min(255, (theme.EditorForeground.B + theme.EditorBackground.B) / 2));

        _exprLabel.ForeColor = theme.EditorForeground;
        _exprLabel.BackColor = theme.EditorBackground;

        _expressionBox.BackColor = theme.EditorBackground;
        _expressionBox.ForeColor = theme.EditorForeground;

        _syntaxLabel.ForeColor = dimColor;
        _syntaxLabel.BackColor = theme.EditorBackground;

        _delimiterHint.ForeColor = dimColor;
        _delimiterHint.BackColor = theme.EditorBackground;

        _examplesHeader.ForeColor = theme.EditorForeground;
        _examplesHeader.BackColor = theme.EditorBackground;

        _examplesList.BackColor = theme.EditorBackground;
        _examplesList.ForeColor = theme.EditorForeground;

        _okButton.FlatStyle = FlatStyle.Flat;
        _okButton.BackColor = theme.EditorBackground;
        _okButton.ForeColor = theme.EditorForeground;

        _cancelButton.FlatStyle = FlatStyle.Flat;
        _cancelButton.BackColor = theme.EditorBackground;
        _cancelButton.ForeColor = theme.EditorForeground;

        ColorizeSedExpression();
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_expressionBox.Text))
            return;

        if (!SedCommandParser.TryParse(_expressionBox.Text, out _))
        {
            MessageBox.Show(this, Strings.SedInvalidExpression, Strings.SedDialogTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _expressionBox.Focus();
            _expressionBox.SelectAll();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── Sed expression colorization ─────────────────────────────────

    private const int WM_SETREDRAW = 0x000B;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void ColorizeSedExpression()
    {
        string text = _expressionBox.Text;
        if (text.Length == 0) return;

        // Save caret position.
        int savedPos = _expressionBox.SelectionStart;
        int savedLen = _expressionBox.SelectionLength;

        // Suppress redraw to prevent flicker.
        SendMessage(_expressionBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

        try
        {
            Color defaultColor = _theme?.EditorForeground ?? ForeColor;
            Color keywordColor = _theme?.GetTokenColor(TokenType.Keyword) ?? Color.Blue;
            Color operatorColor = _theme?.GetTokenColor(TokenType.Operator) ?? Color.Gray;
            Color stringColor = _theme?.GetTokenColor(TokenType.String) ?? Color.Brown;
            Color numberColor = _theme?.GetTokenColor(TokenType.Number) ?? Color.Green;

            // Reset all text to default color.
            _expressionBox.SelectAll();
            _expressionBox.SelectionColor = defaultColor;

            if (text.Length == 0) return;

            // Parse the sed expression: s/pattern/replacement/flags
            int pos = 0;

            // 's' command character.
            if (pos < text.Length && (text[pos] == 's' || text[pos] == 'S'))
            {
                SetColor(pos, 1, keywordColor);
                pos++;
            }
            else
            {
                return;
            }

            // Delimiter character.
            if (pos >= text.Length) return;
            char delim = text[pos];
            SetColor(pos, 1, operatorColor);
            pos++;

            // Pattern: everything up to the next unescaped delimiter.
            int patternStart = pos;
            pos = FindNextDelimiter(text, pos, delim);
            if (patternStart < pos)
                SetColor(patternStart, pos - patternStart, stringColor);

            // Second delimiter.
            if (pos < text.Length && text[pos] == delim)
            {
                SetColor(pos, 1, operatorColor);
                pos++;
            }
            else return;

            // Replacement: everything up to the next unescaped delimiter.
            int replacementStart = pos;
            pos = FindNextDelimiter(text, pos, delim);
            if (replacementStart < pos)
                SetColor(replacementStart, pos - replacementStart, numberColor);

            // Third delimiter.
            if (pos < text.Length && text[pos] == delim)
            {
                SetColor(pos, 1, operatorColor);
                pos++;
            }

            // Flags after the last delimiter.
            if (pos < text.Length)
                SetColor(pos, text.Length - pos, keywordColor);
        }
        finally
        {
            // Restore caret position.
            _expressionBox.SelectionStart = savedPos;
            _expressionBox.SelectionLength = savedLen;

            // Re-enable redraw and refresh.
            SendMessage(_expressionBox.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            _expressionBox.Invalidate();
        }
    }

    private void SetColor(int start, int length, Color color)
    {
        _expressionBox.SelectionStart = start;
        _expressionBox.SelectionLength = length;
        _expressionBox.SelectionColor = color;
    }

    /// <summary>
    /// Finds the next unescaped occurrence of <paramref name="delim"/> starting
    /// at <paramref name="start"/>. Returns the index of the delimiter, or
    /// <c>text.Length</c> if not found.
    /// </summary>
    private static int FindNextDelimiter(string text, int start, char delim)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++; // Skip escaped character.
                continue;
            }
            if (text[i] == delim)
                return i;
        }
        return text.Length;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _expressionBox.Dispose();
            _exprLabel.Dispose();
            _syntaxLabel.Dispose();
            _delimiterHint.Dispose();
            _examplesHeader.Dispose();
            _examplesList.Dispose();
            _okButton.Dispose();
            _cancelButton.Dispose();
        }
        base.Dispose(disposing);
    }
}
