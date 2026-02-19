using System.Drawing;
using Bascanka.Editor.Highlighting;
using Bascanka.Editor.Themes;

namespace Bascanka.App;

/// <summary>
/// Dialog for managing custom highlighting profiles and their rules.
/// </summary>
public sealed class CustomHighlightManagerDialog : Form
{
    private readonly CustomHighlightStore _store;
    private readonly ITheme _theme;

    // Working copy of profiles (to allow cancel without saving).
    private readonly List<CustomHighlightProfile> _profiles;

    // Left panel: profile list + rename.
    private readonly ListBox _profileList;
    private readonly TextBox _profileNameBox;
    private readonly Button _addProfileBtn;
    private readonly Button _deleteProfileBtn;
    private readonly Button _moveProfileUpBtn;
    private readonly Button _moveProfileDownBtn;

    // Right panel: rules grid.
    private readonly DataGridView _rulesGrid;
    private readonly Button _addRuleBtn;
    private readonly Button _deleteRuleBtn;
    private readonly Button _moveUpBtn;
    private readonly Button _moveDownBtn;
    private readonly Button _exportBtn;
    private readonly Button _saveBtn;

    public CustomHighlightManagerDialog(CustomHighlightStore store, ITheme theme)
    {
        _store = store;
        _theme = theme;

        // Deep copy profiles so edits are non-destructive until save.
        _profiles = new List<CustomHighlightProfile>();
        foreach (var p in store.Profiles)
        {
            var copy = new CustomHighlightProfile { Name = p.Name };
            foreach (var r in p.Rules)
                copy.Rules.Add(new CustomHighlightRule
                {
                    Pattern = r.Pattern,
                    Scope = r.Scope,
                    Foreground = r.Foreground,
                    Background = r.Background,
                    BeginPattern = r.BeginPattern,
                    EndPattern = r.EndPattern,
                    Foldable = r.Foldable,
                });
            _profiles.Add(copy);
        }

        // ── Form setup ─────────────────────────────────────────────────
        Text = Strings.CustomHighlightTitle;
        MinimumSize = new Size(840, 480);

        // Restore saved size or use a larger default.
        int savedW = SettingsManager.GetInt(SettingsManager.KeyHighlightDlgWidth, 1120);
        int savedH = SettingsManager.GetInt(SettingsManager.KeyHighlightDlgHeight, 680);
        Size = new Size(Math.Max(savedW, MinimumSize.Width),
                        Math.Max(savedH, MinimumSize.Height));

        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = theme.EditorBackground;
        ForeColor = theme.EditorForeground;

        // Persist size on close.
        FormClosing += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal)
            {
                SettingsManager.SetInt(SettingsManager.KeyHighlightDlgWidth, Size.Width);
                SettingsManager.SetInt(SettingsManager.KeyHighlightDlgHeight, Size.Height);
            }
        };

        // ── Left panel (profiles) ──────────────────────────────────────
        var leftPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 200,
            Padding = new Padding(8),
        };

        var profileLabel = new Label
        {
            Text = Strings.CustomHighlightTitle,
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = theme.EditorForeground,
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
        };

        _profileNameBox = new TextBox
        {
            Dock = DockStyle.Top,
            BackColor = theme.EditorBackground,
            ForeColor = theme.EditorForeground,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(Font.FontFamily, 9.5f),
        };
        _profileNameBox.Leave += OnProfileNameChanged;
        _profileNameBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { OnProfileNameChanged(null, EventArgs.Empty); e.SuppressKeyPress = true; } };

        _profileList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            BackColor = theme.EditorBackground,
            ForeColor = theme.EditorForeground,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _profileList.SelectedIndexChanged += OnProfileSelected;

        var profileBtnPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 68,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0, 4, 0, 0),
        };
        profileBtnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profileBtnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profileBtnPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        profileBtnPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _addProfileBtn = MakeButton(Strings.CustomHighlightAddProfile);
        _addProfileBtn.Dock = DockStyle.Fill;
        _addProfileBtn.AutoSize = false;
        _addProfileBtn.Click += OnAddProfile;
        _deleteProfileBtn = MakeButton(Strings.CustomHighlightDeleteProfile);
        _deleteProfileBtn.Dock = DockStyle.Fill;
        _deleteProfileBtn.AutoSize = false;
        _deleteProfileBtn.Click += OnDeleteProfile;
        _moveProfileUpBtn = MakeButton("\u2191");
        _moveProfileUpBtn.Dock = DockStyle.Fill;
        _moveProfileUpBtn.AutoSize = false;
        _moveProfileUpBtn.Click += OnMoveProfileUp;
        _moveProfileDownBtn = MakeButton("\u2193");
        _moveProfileDownBtn.Dock = DockStyle.Fill;
        _moveProfileDownBtn.AutoSize = false;
        _moveProfileDownBtn.Click += OnMoveProfileDown;

        profileBtnPanel.Controls.Add(_addProfileBtn, 0, 0);
        profileBtnPanel.Controls.Add(_deleteProfileBtn, 1, 0);
        profileBtnPanel.Controls.Add(_moveProfileUpBtn, 0, 1);
        profileBtnPanel.Controls.Add(_moveProfileDownBtn, 1, 1);

        var nameSpacer = new Panel { Dock = DockStyle.Top, Height = 6 };

        leftPanel.Controls.Add(_profileList);
        leftPanel.Controls.Add(nameSpacer);
        leftPanel.Controls.Add(_profileNameBox);
        leftPanel.Controls.Add(profileBtnPanel);
        leftPanel.Controls.Add(profileLabel);

        // ── Right panel (rules) ────────────────────────────────────────
        var rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };

        _rulesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = theme.EditorBackground,
            ForeColor = theme.EditorForeground,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = theme.EditorBackground,
                ForeColor = theme.EditorForeground,
                SelectionBackColor = theme.SelectionBackground,
                SelectionForeColor = theme.EditorForeground,
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = theme.GutterBackground,
                ForeColor = theme.GutterForeground,
            },
            EnableHeadersVisualStyles = false,
            GridColor = Color.FromArgb(60, 60, 60),
            BorderStyle = BorderStyle.FixedSingle,
        };

        // Pattern column.
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Pattern",
            HeaderText = Strings.CustomHighlightPattern,
            Width = 200,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });

        // Scope column (combo).
        var scopeCol = new DataGridViewComboBoxColumn
        {
            Name = "Scope",
            HeaderText = Strings.CustomHighlightScope,
            Width = 80,
            FlatStyle = FlatStyle.Flat,
        };
        scopeCol.Items.AddRange("line", "match", "block");
        _rulesGrid.Columns.Add(scopeCol);

        // End Pattern column (for block rules).
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "EndPattern",
            HeaderText = Strings.CustomHighlightEndPattern,
            Width = 140,
        });

        // Foreground color column.
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Foreground",
            HeaderText = Strings.CustomHighlightForeground,
            Width = 90,
        });

        // Background color column.
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Background",
            HeaderText = Strings.CustomHighlightBackground,
            Width = 90,
        });

        // Foldable column (for block rules).
        var foldableCol = new DataGridViewCheckBoxColumn
        {
            Name = "Foldable",
            HeaderText = Strings.CustomHighlightFoldable,
            Width = 70,
        };
        _rulesGrid.Columns.Add(foldableCol);

        _rulesGrid.CellFormatting += OnCellFormatting;
        _rulesGrid.CellMouseClick += OnCellMouseClick;
        _rulesGrid.CellEndEdit += OnCellEndEdit;
        _rulesGrid.KeyDown += OnGridKeyDown;
        _rulesGrid.SelectionChanged += (_, _) => UpdateButtonStates();
        // Commit checkbox edits immediately so the Value property is always current.
        _rulesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_rulesGrid.IsCurrentCellDirty)
                _rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        // Rule buttons.
        var ruleBtnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 0),
        };

        _addRuleBtn = MakeButton(Strings.CustomHighlightAddRule);
        _addRuleBtn.Click += OnAddRule;
        _deleteRuleBtn = MakeButton(Strings.CustomHighlightDeleteRule);
        _deleteRuleBtn.Click += OnDeleteRule;
        _moveUpBtn = MakeButton("\u2191"); // ↑
        _moveUpBtn.Width = 32;
        _moveUpBtn.Click += OnMoveRuleUp;
        _moveDownBtn = MakeButton("\u2193"); // ↓
        _moveDownBtn.Width = 32;
        _moveDownBtn.Click += OnMoveRuleDown;

        ruleBtnPanel.Controls.Add(_addRuleBtn);
        ruleBtnPanel.Controls.Add(_deleteRuleBtn);
        ruleBtnPanel.Controls.Add(_moveUpBtn);
        ruleBtnPanel.Controls.Add(_moveDownBtn);

        rightPanel.Controls.Add(_rulesGrid);
        rightPanel.Controls.Add(ruleBtnPanel);

        // ── Bottom (Save) ──────────────────────────────────────────────
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 8, 8, 8),
        };

        _saveBtn = MakeButton(Strings.CustomHighlightSave);
        _saveBtn.Width = 100;
        _saveBtn.Click += OnSave;

        var closeBtn = MakeButton(Strings.ButtonCancel);
        closeBtn.Width = 100;
        closeBtn.Click += (_, _) => Close();

        var importBtn = MakeButton(Strings.CustomHighlightImport);
        importBtn.Width = 100;
        importBtn.Click += OnImport;

        _exportBtn = MakeButton(Strings.CustomHighlightExport);
        _exportBtn.Width = 100;
        _exportBtn.Click += OnExport;

        bottomPanel.Controls.Add(closeBtn);
        bottomPanel.Controls.Add(_saveBtn);
        bottomPanel.Controls.Add(importBtn);
        bottomPanel.Controls.Add(_exportBtn);

        // ── Assemble ───────────────────────────────────────────────────
        Controls.Add(rightPanel);
        Controls.Add(leftPanel);
        Controls.Add(bottomPanel);

        RefreshProfileList();
        UpdateButtonStates();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private Button MakeButton(string text)
    {
        return new Button
        {
            Text = text,
            Height = 28,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = _theme.EditorBackground,
            ForeColor = _theme.EditorForeground,
            Font = new Font(Font.FontFamily, 8.5f),
            Margin = new Padding(0, 0, 4, 0),
        };
    }

    private void RefreshProfileList()
    {
        _profileList.Items.Clear();
        foreach (var p in _profiles)
            _profileList.Items.Add(p.Name);
        if (_profileList.Items.Count > 0 && _profileList.SelectedIndex < 0)
            _profileList.SelectedIndex = 0;
    }

    private CustomHighlightProfile? SelectedProfile =>
        _profileList.SelectedIndex >= 0 && _profileList.SelectedIndex < _profiles.Count
            ? _profiles[_profileList.SelectedIndex]
            : null;

    private void LoadRulesIntoGrid(CustomHighlightProfile? profile)
    {
        _rulesGrid.Rows.Clear();
        if (profile is null) return;

        foreach (var rule in profile.Rules)
        {
            bool isBlock = string.Equals(rule.Scope, "block", StringComparison.OrdinalIgnoreCase);
            int rowIndex = _rulesGrid.Rows.Add();
            var row = _rulesGrid.Rows[rowIndex];
            row.Cells["Pattern"].Value = isBlock ? rule.BeginPattern : rule.Pattern;
            row.Cells["Scope"].Value = rule.Scope;
            row.Cells["EndPattern"].Value = isBlock ? rule.EndPattern : string.Empty;
            row.Cells["Foreground"].Value = FormatColor(rule.Foreground);
            row.Cells["Background"].Value = FormatColor(rule.Background);
            row.Cells["Foldable"].Value = isBlock && rule.Foldable;
        }
    }

    private void SyncRulesFromGrid()
    {
        var profile = SelectedProfile;
        if (profile is null) return;

        profile.Rules.Clear();
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            string scope = row.Cells["Scope"].Value?.ToString() ?? "match";
            bool isBlock = string.Equals(scope, "block", StringComparison.OrdinalIgnoreCase);

            var rule = new CustomHighlightRule
            {
                Scope = scope,
                Foreground = ParseColor(row.Cells["Foreground"].Value?.ToString()),
                Background = ParseColor(row.Cells["Background"].Value?.ToString()),
            };

            if (isBlock)
            {
                rule.BeginPattern = row.Cells["Pattern"].Value?.ToString() ?? string.Empty;
                rule.EndPattern = row.Cells["EndPattern"].Value?.ToString() ?? string.Empty;
                rule.Foldable = row.Cells["Foldable"].Value is true;
            }
            else
            {
                rule.Pattern = row.Cells["Pattern"].Value?.ToString() ?? string.Empty;
            }

            profile.Rules.Add(rule);
        }
    }

    private static string FormatColor(Color c)
    {
        if (c.IsEmpty || c == Color.Empty) return string.Empty;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static Color ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Color.Empty;
        try
        {
            if (hex.StartsWith('#') && hex.Length == 7)
            {
                int r = Convert.ToInt32(hex.Substring(1, 2), 16);
                int g = Convert.ToInt32(hex.Substring(3, 2), 16);
                int b = Convert.ToInt32(hex.Substring(5, 2), 16);
                return Color.FromArgb(r, g, b);
            }
            return ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return Color.Empty;
        }
    }

    private void UpdateButtonStates()
    {
        bool hasProfile = SelectedProfile is not null;
        bool hasRule = hasProfile && _rulesGrid.CurrentRow is not null;

        _deleteProfileBtn.Enabled = hasProfile;
        _moveProfileUpBtn.Enabled = hasProfile && _profileList.SelectedIndex > 0;
        _moveProfileDownBtn.Enabled = hasProfile && _profileList.SelectedIndex < _profiles.Count - 1;
        _profileNameBox.Enabled = hasProfile;
        _exportBtn.Enabled = hasProfile;
        _addRuleBtn.Enabled = hasProfile;
        _deleteRuleBtn.Enabled = hasRule;
        _moveUpBtn.Enabled = hasRule;
        _moveDownBtn.Enabled = hasRule;
    }

    // ── Event handlers ──────────────────────────────────────────────

    private void OnProfileSelected(object? sender, EventArgs e)
    {
        var profile = SelectedProfile;
        _profileNameBox.Text = profile?.Name ?? string.Empty;
        LoadRulesIntoGrid(profile);
        UpdateButtonStates();
    }

    private void OnProfileNameChanged(object? sender, EventArgs e)
    {
        var profile = SelectedProfile;
        if (profile is null) return;

        string newName = _profileNameBox.Text.Trim();
        if (string.IsNullOrEmpty(newName) || newName == profile.Name) return;

        profile.Name = newName;
        int idx = _profileList.SelectedIndex;
        _profileList.Items[idx] = newName;
    }

    private void OnAddProfile(object? sender, EventArgs e)
    {
        var profile = new CustomHighlightProfile
        {
            Name = Strings.CustomHighlightNewProfile + " " + (_profiles.Count + 1),
        };
        _profiles.Add(profile);
        RefreshProfileList();
        _profileList.SelectedIndex = _profiles.Count - 1;
    }

    private void OnDeleteProfile(object? sender, EventArgs e)
    {
        int idx = _profileList.SelectedIndex;
        if (idx < 0 || idx >= _profiles.Count) return;
        _profiles.RemoveAt(idx);
        RefreshProfileList();
    }

    private void OnMoveProfileUp(object? sender, EventArgs e)
    {
        int idx = _profileList.SelectedIndex;
        if (idx <= 0) return;
        SyncRulesFromGrid();
        (_profiles[idx - 1], _profiles[idx]) = (_profiles[idx], _profiles[idx - 1]);
        RefreshProfileList();
        _profileList.SelectedIndex = idx - 1;
    }

    private void OnMoveProfileDown(object? sender, EventArgs e)
    {
        int idx = _profileList.SelectedIndex;
        if (idx < 0 || idx >= _profiles.Count - 1) return;
        SyncRulesFromGrid();
        (_profiles[idx], _profiles[idx + 1]) = (_profiles[idx + 1], _profiles[idx]);
        RefreshProfileList();
        _profileList.SelectedIndex = idx + 1;
    }

    private void OnAddRule(object? sender, EventArgs e)
    {
        var profile = SelectedProfile;
        if (profile is null) return;
        SyncRulesFromGrid();
        profile.Rules.Add(new CustomHighlightRule { Pattern = "pattern", Scope = "match" });
        LoadRulesIntoGrid(profile);
    }

    private void OnDeleteRule(object? sender, EventArgs e)
    {
        if (_rulesGrid.CurrentRow is null) return;
        var profile = SelectedProfile;
        if (profile is null) return;
        SyncRulesFromGrid();
        int idx = _rulesGrid.CurrentRow.Index;
        if (idx >= 0 && idx < profile.Rules.Count)
        {
            profile.Rules.RemoveAt(idx);
            LoadRulesIntoGrid(profile);
        }
    }

    private void OnMoveRuleUp(object? sender, EventArgs e)
    {
        if (_rulesGrid.CurrentRow is null) return;
        int idx = _rulesGrid.CurrentRow.Index;
        if (idx <= 0) return;
        var profile = SelectedProfile;
        if (profile is null) return;
        SyncRulesFromGrid();
        (profile.Rules[idx - 1], profile.Rules[idx]) = (profile.Rules[idx], profile.Rules[idx - 1]);
        LoadRulesIntoGrid(profile);
        _rulesGrid.CurrentCell = _rulesGrid.Rows[idx - 1].Cells[0];
    }

    private void OnMoveRuleDown(object? sender, EventArgs e)
    {
        if (_rulesGrid.CurrentRow is null) return;
        int idx = _rulesGrid.CurrentRow.Index;
        var profile = SelectedProfile;
        if (profile is null) return;
        SyncRulesFromGrid();
        if (idx >= profile.Rules.Count - 1) return;
        (profile.Rules[idx], profile.Rules[idx + 1]) = (profile.Rules[idx + 1], profile.Rules[idx]);
        LoadRulesIntoGrid(profile);
        _rulesGrid.CurrentCell = _rulesGrid.Rows[idx + 1].Cells[0];
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        string colName = _rulesGrid.Columns[e.ColumnIndex].Name;

        if (colName is "Foreground" or "Background")
        {
            string hex = e.Value?.ToString() ?? string.Empty;
            Color c = ParseColor(hex);
            if (c != Color.Empty)
            {
                e.CellStyle!.BackColor = c;
                int brightness = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                e.CellStyle.ForeColor = brightness > 128 ? Color.Black : Color.White;
            }
        }

        // Gray out EndPattern and Foldable cells for non-block rules.
        if (colName is "EndPattern" or "Foldable")
        {
            string scope = _rulesGrid.Rows[e.RowIndex].Cells["Scope"].Value?.ToString() ?? "match";
            bool isBlock = string.Equals(scope, "block", StringComparison.OrdinalIgnoreCase);
            if (!isBlock)
            {
                e.CellStyle!.BackColor = Color.FromArgb(40, 40, 40);
                e.CellStyle.ForeColor = Color.FromArgb(80, 80, 80);
            }
        }
    }

    private void OnCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0) return;
        string colName = _rulesGrid.Columns[e.ColumnIndex].Name;
        if (colName is not ("Foreground" or "Background")) return;

        var cell = _rulesGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];

        if (e.Button == MouseButtons.Left)
        {
            PickColorForCell(cell);
        }
        else if (e.Button == MouseButtons.Right)
        {
            var ctx = new ContextMenuStrip { Renderer = new ThemedMenuRenderer(_theme) };
            ctx.Items.Add(Strings.CustomHighlightPickColor, null, (_, _) => PickColorForCell(cell));
            ctx.Items.Add(Strings.CustomHighlightClearColor, null, (_, _) =>
            {
                cell.Value = string.Empty;
                _rulesGrid.InvalidateRow(e.RowIndex);
                SyncRulesFromGrid();
            });
            var cellRect = _rulesGrid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
            ctx.Show(_rulesGrid, cellRect.X + e.X, cellRect.Y + e.Y);
        }
    }

    private void PickColorForCell(DataGridViewCell cell)
    {
        string current = cell.Value?.ToString() ?? "";
        Color initial = ParseColor(current);

        using var dlg = new ColorDialog
        {
            FullOpen = true,
            Color = initial != Color.Empty ? initial : Color.White,
        };

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            cell.Value = FormatColor(dlg.Color);
            _rulesGrid.InvalidateRow(cell.RowIndex);
            SyncRulesFromGrid();
        }
    }

    private void OnCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        // Keep in-memory model in sync after text edits.
        SyncRulesFromGrid();
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_rulesGrid.CurrentCell is null) return;
        if (e.KeyCode is not (Keys.Delete or Keys.Back)) return;

        string colName = _rulesGrid.Columns[_rulesGrid.CurrentCell.ColumnIndex].Name;
        if (colName is "Foreground" or "Background")
        {
            _rulesGrid.CurrentCell.Value = string.Empty;
            _rulesGrid.InvalidateRow(_rulesGrid.CurrentCell.RowIndex);
            SyncRulesFromGrid();
            e.Handled = true;
        }
    }

    private void OnExport(object? sender, EventArgs e)
    {
        SyncRulesFromGrid();

        var profile = SelectedProfile;
        if (profile is null)
        {
            MessageBox.Show(this, Strings.CustomHighlightExportEmpty,
                Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = Strings.CustomHighlightExport,
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            FileName = profile.Name + ".json",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string json = CustomHighlightStore.ExportToJson([profile]);
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show(this, Strings.CustomHighlightExportSuccess,
                Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImport(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = Strings.CustomHighlightImport,
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            string json = File.ReadAllText(dlg.FileName);
            var imported = CustomHighlightStore.ImportFromJson(json);
            if (imported is null || imported.Count == 0)
            {
                MessageBox.Show(this, Strings.CustomHighlightImportError,
                    Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SyncRulesFromGrid();

            foreach (var profile in imported)
            {
                // If a profile with the same name exists, replace it.
                int existing = _profiles.FindIndex(p =>
                    string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                    _profiles[existing] = profile;
                else
                    _profiles.Add(profile);
            }

            RefreshProfileList();
            MessageBox.Show(this,
                string.Format(Strings.CustomHighlightImportSuccess, imported.Count),
                Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        SyncRulesFromGrid();
        _store.SetProfiles(_profiles);
        _store.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}
