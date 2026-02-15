using System.Reflection;

namespace Bascanka.App;

/// <summary>
/// About dialog showing application info, the Bašćanska ploča image,
/// and credits.
/// </summary>
internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = Strings.MenuAbout.Replace("&", "");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);
        ClientSize = new Size(520, 800);

        // ── Image ────────────────────────────────────────────────────
        var pictureBox = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Top,
            Height = 240,
            Padding = new Padding(20, 0, 20, 0),
            BackColor = Color.FromArgb(30, 30, 30),
        };

        // Load the embedded resource image.
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("Bascanka.App.Resources.bascanska_ploca.jpg");
        if (stream is not null)
            pictureBox.Image = Image.FromStream(stream);

        // ── Title ────────────────────────────────────────────────────
        var titleLabel = new Label
        {
            Text = "Bascanka",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(86, 156, 214),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(0, 4, 0, 0)
        };

        // ── Version ──────────────────────────────────────────────────
        string version = asm.GetName().Version?.ToString(3) ?? "1.0.0";
        var versionLabel = new Label
        {
            Text = $"Version {version}",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(150, 150, 150),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 22,
        };

        // ── Copyright ────────────────────────────────────────────────
        var copyrightLabel = new Label
        {
            Text = "\u00a9 2026 Josip Habjan. All rights reserved.",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(130, 130, 130),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 20,
        };

        // ── Description ──────────────────────────────────────────────
        var descLabel = new Label
        {
            Text = "Open source text editor for Windows.",
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(200, 200, 200),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(0, 4, 0, 0),
        };

        // ── License ────────────────────────────────────────────────
        var licenseLabel = new Label
        {
            Text = "GNU GENERAL PUBLIC LICENSE Version 3",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(130, 130, 130),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 20,
        };

        // ── Separator ────────────────────────────────────────────────
        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(60, 60, 60),
            Margin = new Padding(20, 6, 20, 6),
        };

        // ── Origin text ──────────────────────────────────────────────
        var originLabel = new Label
        {
            Text = "The name \"Bascanka\" comes from the Bašćanska ploča " +
                   "(Baška tablet) - a stone tablet from around 1100 AD, " +
                   "found in the Church of St. Lucy near Baška on the island " +
                   "of Krk, Croatia. It is one of the oldest known inscriptions " +
                   "in the Croatian language, written in Glagolitic script. The " +
                   "tablet documents a royal land donation by King Zvonimir and " +
                   "is a cornerstone of Croatian cultural heritage and literacy.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(180, 180, 180),
            TextAlign = ContentAlignment.TopCenter,
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Padding = new Padding(24, 12, 24, 12),
        };

        // ── Author (panel with static text + clickable email) ────────
        var authorPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Color.FromArgb(30, 30, 30),
            Padding = new Padding(0, 6, 0, 12),
        };

        var authorFont = new Font("Segoe UI", 9.5f);
        var emailFont = new Font("Segoe UI", 9.5f, FontStyle.Underline);

        const string authorPrefix = "Contact author: ";
        const string email = "habjan@gmail.com";
        const string authorSuffix = "";

        var authorPrefixLabel = new Label
        {
            Text = authorPrefix,
            Font = authorFont,
            ForeColor = Color.FromArgb(200, 200, 200),
            AutoSize = true,
        };

        var emailLink = new Label
        {
            Text = email,
            Font = emailFont,
            ForeColor = Color.FromArgb(86, 156, 214),
            AutoSize = true,
            Cursor = Cursors.Hand,
        };
        emailLink.Click += (_, _) =>
        {
            Clipboard.SetText(email);
            var saved = emailLink.Text;
            emailLink.Text = "Copied!";
            emailLink.ForeColor = Color.FromArgb(78, 201, 176);
            var timer = new System.Windows.Forms.Timer { Interval = 1500 };
            timer.Tick += (_, _) =>
            {
                emailLink.Text = saved;
                emailLink.ForeColor = Color.FromArgb(86, 156, 214);
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        };

        var authorSuffixLabel = new Label
        {
            Text = authorSuffix,
            Font = authorFont,
            ForeColor = Color.FromArgb(200, 200, 200),
            AutoSize = true,
        };

        // Position the three labels centered within the panel.
        authorPanel.Layout += (_, _) =>
        {
            int totalWidth = authorPrefixLabel.Width + emailLink.Width + authorSuffixLabel.Width;
            int x = (authorPanel.ClientSize.Width - totalWidth) / 2;
            int y = authorPanel.Padding.Top;
            authorPrefixLabel.Location = new Point(x, y);
            emailLink.Location = new Point(x + authorPrefixLabel.Width, y);
            authorSuffixLabel.Location = new Point(x + authorPrefixLabel.Width + emailLink.Width, y);
        };

        authorPanel.Controls.Add(authorSuffixLabel);
        authorPanel.Controls.Add(emailLink);
        authorPanel.Controls.Add(authorPrefixLabel);

        // ── Application logo ─────────────────────────────────────────
        var logoPicture = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Top,
            Height = 128,
            Padding = new Padding(0, 0, 0, 0),
            BackColor = Color.FromArgb(30, 30, 30),
        };
        using var logoStream = asm.GetManifestResourceStream("Bascanka.App.Resources.bascanka_logo.png");
        if (logoStream is not null)
            logoPicture.Image = Image.FromStream(logoStream);

        // ── OK button ────────────────────────────────────────────────
        var okButton = new Button
        {
            Text = Strings.ButtonOK,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 9.5f),
            Size = new Size(90, 32),
            Anchor = AnchorStyles.Bottom,
        };
        okButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        okButton.Location = new Point((ClientSize.Width - 90) / 2, ClientSize.Height - 46);

        AcceptButton = okButton;
        CancelButton = okButton;

        // ── Add controls (reverse order for Dock.Top stacking) ───────
        // Bottom-most first: OK button, then image, then content, then logo at top.
        var spacer = new Label { Dock = DockStyle.Top, Height = 16, BackColor = BackColor };

        Controls.Add(okButton);
        Controls.Add(spacer);
        Controls.Add(pictureBox);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 16, BackColor = BackColor });
        Controls.Add(authorPanel);
        Controls.Add(originLabel);
        Controls.Add(separator);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 8, BackColor = BackColor });
        Controls.Add(licenseLabel);
        Controls.Add(descLabel);
        Controls.Add(copyrightLabel);
        Controls.Add(versionLabel);
        Controls.Add(titleLabel);
        Controls.Add(logoPicture);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 16, BackColor = BackColor });
    }
}
