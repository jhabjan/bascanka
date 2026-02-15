using System.Reflection;

namespace Bascanka.App;

/// <summary>
/// About dialog showing application info, the Bašćanska ploča image,
/// credits on the left, and release notes on the right.
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
        ClientSize = new Size(1060, 800);

        var asm = Assembly.GetExecutingAssembly();

        // ── Bottom bar (OK button) ────────────────────────────────────
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = BackColor,
        };

        var okButton = new Button
        {
            Text = Strings.ButtonOK,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Segoe UI", 9.5f),
            Size = new Size(90, 32),
        };
        okButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);

        bottomPanel.Layout += (_, _) =>
        {
            okButton.Location = new Point(
                (bottomPanel.ClientSize.Width - okButton.Width) / 2,
                (bottomPanel.Height - okButton.Height) / 2);
        };
        bottomPanel.Controls.Add(okButton);

        AcceptButton = okButton;
        CancelButton = okButton;

        // ── Left panel (about info) ───────────────────────────────────
        var leftPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 520,
            BackColor = BackColor,
            AutoScroll = true,
        };

        // Application logo.
        var logoPicture = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Top,
            Height = 128,
            BackColor = BackColor,
        };
        using var logoStream = asm.GetManifestResourceStream("Bascanka.App.Resources.bascanka_logo.png");
        if (logoStream is not null)
            logoPicture.Image = Image.FromStream(logoStream);

        // Title.
        var titleLabel = new Label
        {
            Text = "Bascanka",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(86, 156, 214),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(0, 4, 0, 0),
        };

        // Version.
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

        // Copyright.
        var copyrightLabel = new Label
        {
            Text = "\u00a9 2026 Josip Habjan. All rights reserved.",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(130, 130, 130),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 20,
        };

        // Description.
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

        // License.
        var licenseLabel = new Label
        {
            Text = "GNU GENERAL PUBLIC LICENSE Version 3",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(130, 130, 130),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 20,
        };

        // Separator.
        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(60, 60, 60),
        };

        // Origin text.
        var originLabel = new Label
        {
            Text = "The name \"Bascanka\" comes from the Ba\u0161\u0107anska plo\u010da " +
                   "(Ba\u0161ka tablet) - a stone tablet from around 1100 AD, " +
                   "found in the Church of St. Lucy near Ba\u0161ka on the island " +
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

        // Author panel with clickable email.
        var authorPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = BackColor,
            Padding = new Padding(0, 6, 0, 12),
        };

        var authorFont = new Font("Segoe UI", 9.5f);
        var emailFont = new Font("Segoe UI", 9.5f, FontStyle.Underline);

        const string authorPrefix = "Contact author: ";
        const string email = "habjan@gmail.com";

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

        authorPanel.Layout += (_, _) =>
        {
            int totalWidth = authorPrefixLabel.Width + emailLink.Width;
            int x = (authorPanel.ClientSize.Width - totalWidth) / 2;
            int y = authorPanel.Padding.Top;
            authorPrefixLabel.Location = new Point(x, y);
            emailLink.Location = new Point(x + authorPrefixLabel.Width, y);
        };

        authorPanel.Controls.Add(emailLink);
        authorPanel.Controls.Add(authorPrefixLabel);

        // Bašćanska ploča image.
        var pictureBox = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Top,
            Height = 240,
            Padding = new Padding(20, 0, 20, 0),
            BackColor = BackColor,
        };
        using var stream = asm.GetManifestResourceStream("Bascanka.App.Resources.bascanska_ploca.jpg");
        if (stream is not null)
            pictureBox.Image = Image.FromStream(stream);

        // Assemble left panel (reverse order for Dock.Top stacking).
        leftPanel.Controls.Add(pictureBox);
        leftPanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 16, BackColor = BackColor });
        leftPanel.Controls.Add(authorPanel);
        leftPanel.Controls.Add(originLabel);
        leftPanel.Controls.Add(separator);
        leftPanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 8, BackColor = BackColor });
        leftPanel.Controls.Add(licenseLabel);
        leftPanel.Controls.Add(descLabel);
        leftPanel.Controls.Add(copyrightLabel);
        leftPanel.Controls.Add(versionLabel);
        leftPanel.Controls.Add(titleLabel);
        leftPanel.Controls.Add(logoPicture);
        leftPanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = 16, BackColor = BackColor });

        // ── Vertical separator ────────────────────────────────────────
        var vertSeparator = new Panel
        {
            Dock = DockStyle.Left,
            Width = 1,
            BackColor = Color.FromArgb(60, 60, 60),
        };

        // ── Right panel (release notes) ───────────────────────────────
        var rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            Padding = new Padding(12, 16, 12, 8),
        };

        // Contributors.
        string contributorsText = "";
        using var contribStream = asm.GetManifestResourceStream("Bascanka.App.Resources.contributors.txt");
        if (contribStream is not null)
        {
            using var reader = new StreamReader(contribStream);
            contributorsText = reader.ReadToEnd();
        }

        var contributorsBox = new RichTextBox
        {
            Text = contributorsText,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Top,
            Height = 180,
            Font = new Font("Consolas", 8.5f),
            BackColor = Color.FromArgb(25, 25, 25),
            ForeColor = Color.FromArgb(180, 180, 180),
            BorderStyle = BorderStyle.None,
            DetectUrls = true,
        };
        contributorsBox.LinkClicked += (_, e) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.LinkText!) { UseShellExecute = true }); }
            catch { }
        };
        contributorsBox.Select(0, 0);

        var contribSpacer = new Panel
        {
            Dock = DockStyle.Top,
            Height = 16,
            BackColor = BackColor,
        };

        // Release notes.
        string releaseNotesText = "";
        using var rnStream = asm.GetManifestResourceStream("Bascanka.App.Resources.release_notes.txt");
        if (rnStream is not null)
        {
            using var reader = new StreamReader(rnStream);
            releaseNotesText = reader.ReadToEnd();
        }

        var releaseNotesBox = new RichTextBox
        {
            Text = releaseNotesText,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f),
            BackColor = Color.FromArgb(25, 25, 25),
            ForeColor = Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.None,
            DetectUrls = true,
        };
        releaseNotesBox.LinkClicked += (_, e) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.LinkText!) { UseShellExecute = true }); }
            catch { }
        };
        releaseNotesBox.SelectAll();
        releaseNotesBox.SelectionTabs = [28, 56, 84, 112];
        releaseNotesBox.Select(0, 0);

        // Focus the OK button when the form is shown.
        Shown += (_, _) => okButton.Focus();

        rightPanel.Controls.Add(releaseNotesBox);
        rightPanel.Controls.Add(contribSpacer);
        rightPanel.Controls.Add(contributorsBox);

        // ── Assemble form ─────────────────────────────────────────────
        Controls.Add(rightPanel);
        Controls.Add(vertSeparator);
        Controls.Add(leftPanel);
        Controls.Add(bottomPanel);
    }
}
