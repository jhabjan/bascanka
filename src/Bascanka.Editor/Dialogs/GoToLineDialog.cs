using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Dialogs;

/// <summary>
/// A simple dialog that prompts the user for a line number within a valid
/// range, validates the input, and exposes the result via <see cref="LineNumber"/>.
/// </summary>
public class GoToLineDialog : Form
{
    // ── Controls ──────────────────────────────────────────────────────
    private readonly Label _promptLabel;
    private readonly Label _rangeLabel;
    private readonly TextBox _lineNumberBox;
    private readonly Button _btnOk;
    private readonly Button _btnCancel;

    // ── State ─────────────────────────────────────────────────────────
    private readonly long _maxLine;
    private readonly ITheme _theme;

    /// <summary>
    /// The line number entered by the user, or <see langword="null"/> if the
    /// dialog was cancelled.
    /// </summary>
    public long? LineNumber { get; private set; }

    // ── Construction ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a new Go To Line dialog.
    /// </summary>
    /// <param name="maxLine">
    /// The maximum valid line number (inclusive).  Typically
    /// <c>document.LineCount</c>.
    /// </param>
    /// <param name="currentLine">
    /// The current caret line, pre-filled in the text box.
    /// </param>
    public GoToLineDialog(long maxLine, long currentLine = 1)
    {
        _maxLine = Math.Max(1, maxLine);
        _theme = ThemeManager.Instance.CurrentTheme;
        LineNumber = null;

        // ── Form properties ───────────────────────────────────────────
        Text = "Go To Line";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(300, 150);
        AcceptButton = null; // set after button creation
        KeyPreview = true;
        BackColor = _theme.EditorBackground;
        ForeColor = _theme.EditorForeground;

        // ── Prompt label ──────────────────────────────────────────────
        _promptLabel = new Label
        {
            Text = "Line number:",
            Location = new Point(12, 14),
            AutoSize = true,
            ForeColor = _theme.EditorForeground,
            BackColor = _theme.EditorBackground,
        };

        // ── Range label ───────────────────────────────────────────────
        _rangeLabel = new Label
        {
            Text = $"(1 \u2013 {_maxLine})",
            Location = new Point(12, 66),
            AutoSize = true,
            BackColor = _theme.EditorBackground,
        };

        // ── Line number text box ──────────────────────────────────────
        _lineNumberBox = new TextBox
        {
            Location = new Point(12, 36),
            Width = 276,
            Text = currentLine.ToString(),
            MaxLength = 18, // enough for long.MaxValue
            BackColor = _theme.FindPanelBackground,
            ForeColor = _theme.EditorForeground,
        };
        _lineNumberBox.SelectAll();
        _lineNumberBox.KeyPress += OnLineNumberKeyPress;
        _lineNumberBox.TextChanged += OnLineNumberTextChanged;

        // ── OK button ─────────────────────────────────────────────────
        _btnOk = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(132, 112),
            Size = new Size(75, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = _theme.EditorBackground,
            ForeColor = _theme.EditorForeground,
        };
        _btnOk.FlatAppearance.BorderColor = _theme.TabBorder;
        _btnOk.Click += OnOkClick;

        // ── Cancel button ─────────────────────────────────────────────
        _btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(213, 112),
            Size = new Size(75, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = _theme.EditorBackground,
            ForeColor = _theme.EditorForeground,
        };
        _btnCancel.FlatAppearance.BorderColor = _theme.TabBorder;

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        // ── Layout ────────────────────────────────────────────────────
        Controls.AddRange([_promptLabel, _lineNumberBox, _rangeLabel, _btnOk, _btnCancel]);

        // Initial validation.
        ValidateInput();
    }

    // ── Validation ────────────────────────────────────────────────────

    /// <summary>
    /// Restricts input to digits and control characters only.
    /// </summary>
    private void OnLineNumberKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
        {
            e.Handled = true;
        }
    }

    private void OnLineNumberTextChanged(object? sender, EventArgs e)
    {
        ValidateInput();
    }

    private void ValidateInput()
    {
        bool isValid = long.TryParse(_lineNumberBox.Text, out long value)
                       && value >= 1
                       && value <= _maxLine;

        _btnOk.Enabled = isValid;

        Color dimColor = Color.FromArgb(
            (_theme.EditorForeground.R + _theme.EditorBackground.R) / 2,
            (_theme.EditorForeground.G + _theme.EditorBackground.G) / 2,
            (_theme.EditorForeground.B + _theme.EditorBackground.B) / 2);

        _rangeLabel.ForeColor = isValid || string.IsNullOrEmpty(_lineNumberBox.Text)
            ? dimColor
            : Color.IndianRed;
    }

    // ── OK handling ───────────────────────────────────────────────────

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (long.TryParse(_lineNumberBox.Text, out long value)
            && value >= 1
            && value <= _maxLine)
        {
            LineNumber = value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// Shows the dialog modally and returns the selected line number,
    /// or <see langword="null"/> if the user cancelled.
    /// </summary>
    public static long? Show(IWin32Window? owner, long maxLine, long currentLine = 1)
    {
        using var dialog = new GoToLineDialog(maxLine, currentLine);
        DialogResult result = dialog.ShowDialog(owner);
        return result == DialogResult.OK ? dialog.LineNumber : null;
    }
}
