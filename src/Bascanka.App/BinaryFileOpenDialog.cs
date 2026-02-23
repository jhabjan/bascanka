using Bascanka.Editor.Themes;

namespace Bascanka.App;

/// <summary>
/// Modal dialog shown when a binary file is detected, asking the user
/// whether to open it as Hex or Text, with an option to remember
/// the choice per file extension.
/// </summary>
internal sealed class BinaryFileOpenDialog : Form
{
    private readonly Label _messageLabel;
    private readonly CheckBox _rememberCheckBox;
    private readonly Button _hexButton;
    private readonly Button _textButton;
    private readonly Button _cancelButton;

    /// <summary>True if the user chose Hex mode; false if Text mode.</summary>
    public bool IsHexMode { get; private set; }

    /// <summary>True if the user checked "Remember for this extension".</summary>
    public bool RememberChoice => _rememberCheckBox.Checked;

    public BinaryFileOpenDialog(string fileName, string extension)
    {
        Text = Strings.AppTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(460, 220);
        ShowInTaskbar = false;

        int margin = 20;
        int y = 16;

        _messageLabel = new Label
        {
            Text = string.Format(Strings.BinaryFileDetected, fileName),
            Font = new Font("Segoe UI", 10f),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Location = new Point(margin, y),
        };
        Controls.Add(_messageLabel);
        y += _messageLabel.PreferredHeight + 16;

        bool hasExtension = !string.IsNullOrEmpty(extension);
        _rememberCheckBox = new CheckBox
        {
            Text = hasExtension
                ? string.Format(Strings.BinaryRememberForExt, extension)
                : Strings.BinaryRememberNoExt,
            Font = new Font("Segoe UI", 9.5f),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Location = new Point(margin, y),
            Enabled = hasExtension,
        };
        Controls.Add(_rememberCheckBox);
        y += _rememberCheckBox.PreferredSize.Height + 20;

        int btnWidth = 120;
        int btnHeight = 34;
        int spacing = 10;
        int totalWidth = btnWidth * 3 + spacing * 2;
        int startX = (ClientSize.Width - totalWidth) / 2;

        _hexButton = new Button
        {
            Text = Strings.BinaryOpenAsHex,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
            Size = new Size(btnWidth, btnHeight),
            Location = new Point(startX, y),
        };
        _hexButton.Click += (_, _) =>
        {
            IsHexMode = true;
            DialogResult = DialogResult.OK;
        };
        Controls.Add(_hexButton);

        _textButton = new Button
        {
            Text = Strings.BinaryOpenAsText,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
            Size = new Size(btnWidth, btnHeight),
            Location = new Point(startX + btnWidth + spacing, y),
        };
        _textButton.Click += (_, _) =>
        {
            IsHexMode = false;
            DialogResult = DialogResult.OK;
        };
        Controls.Add(_textButton);

        _cancelButton = new Button
        {
            Text = Strings.ButtonCancel,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
            Size = new Size(btnWidth, btnHeight),
            Location = new Point(startX + (btnWidth + spacing) * 2, y),
        };
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
        };
        Controls.Add(_cancelButton);

        CancelButton = _cancelButton;

        // Adjust form height to fit content.
        ClientSize = new Size(ClientSize.Width, y + btnHeight + 16);
    }

    public void ApplyTheme(ITheme theme)
    {
        BackColor = theme.EditorBackground;
        ForeColor = theme.EditorForeground;

        _messageLabel.ForeColor = theme.EditorForeground;
        _rememberCheckBox.ForeColor = theme.EditorForeground;

        foreach (var btn in new[] { _hexButton, _textButton, _cancelButton })
        {
            btn.BackColor = theme.FindPanelBackground;
            btn.ForeColor = theme.EditorForeground;
            btn.FlatAppearance.BorderColor = theme.TabBorder;
        }
    }
}
