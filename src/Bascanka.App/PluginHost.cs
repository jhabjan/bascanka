using System.Reflection;
using System.Runtime.Loader;
using Bascanka.Plugins.Api;
using KeyEventArgs2 = Bascanka.Plugins.Api.KeyEventArgs2;

namespace Bascanka.App;

/// <summary>
/// Loads, manages, and unloads Bascanka plugins.
/// Each plugin DLL is loaded into its own collectible <see cref="AssemblyLoadContext"/>
/// to support hot-reloading. Also loads .csx script plugins via <see cref="ScriptCompiler"/>.
/// Implements all host-side plugin API interfaces.
/// </summary>
public sealed class PluginHost(MainForm form) : IEditorHost, IMenuApi, IPanelApi, IBufferApi, IDocumentApi, IStatusBarApi
{
    private readonly MainForm _form = form;
    private readonly List<LoadedPlugin> _plugins = [];
    private readonly ScriptCompiler _scriptCompiler = new();

    private static readonly string PluginsDirectory =
        Path.Combine(AppContext.BaseDirectory, "plugins");

    // ── IEditorHost ──────────────────────────────────────────────────

    public IMenuApi Menu => this;
    public IPanelApi Panels => this;
    public IBufferApi ActiveBuffer => this;
    public IDocumentApi Documents => this;
    public IStatusBarApi StatusBar => this;

    public event EventHandler<DocumentEventArgs>? DocumentOpened;
    public event EventHandler<DocumentEventArgs>? DocumentClosed;
    public event EventHandler<DocumentEventArgs>? DocumentSaved;
    public event EventHandler<TextChangedEventArgs>? TextChanged;
    public event EventHandler<KeyEventArgs2>? KeyDown;

    public void ShowMessage(string message)
    {
        MessageBox.Show(_form, message, Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public string? ShowInputDialog(string prompt, string defaultValue = "")
    {
        // Simple input dialog using InputBox pattern.
        using var dialog = new Form
        {
            Text = Strings.AppTitle,
            Size = new Size(400, 160),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };

        var label = new Label { Text = prompt, Left = 12, Top = 12, Width = 360 };
        var textBox = new TextBox { Text = defaultValue, Left = 12, Top = 40, Width = 360 };
        var btnOk = new Button
        {
            Text = Strings.ButtonOK,
            DialogResult = DialogResult.OK,
            Left = 216,
            Top = 80,
            Width = 75,
        };
        var btnCancel = new Button
        {
            Text = Strings.ButtonCancel,
            DialogResult = DialogResult.Cancel,
            Left = 297,
            Top = 80,
            Width = 75,
        };

        dialog.Controls.AddRange([label, textBox, btnOk, btnCancel]);
        dialog.AcceptButton = btnOk;
        dialog.CancelButton = btnCancel;

        return dialog.ShowDialog(_form) == DialogResult.OK ? textBox.Text : null;
    }

    // ── Event raising (called by MainForm) ───────────────────────────

    internal void RaiseDocumentOpened(string path) =>
        DocumentOpened?.Invoke(this, new DocumentEventArgs(path));

    internal void RaiseDocumentClosed(string path) =>
        DocumentClosed?.Invoke(this, new DocumentEventArgs(path));

    internal void RaiseDocumentSaved(string path) =>
        DocumentSaved?.Invoke(this, new DocumentEventArgs(path));

    internal void RaiseTextChanged(long offset, long oldLength, long newLength) =>
        TextChanged?.Invoke(this, new TextChangedEventArgs(offset, oldLength, newLength));

    internal void RaiseKeyDown(KeyEventArgs2 args) =>
        KeyDown?.Invoke(this, args);

    // ── IMenuApi ─────────────────────────────────────────────────────

    void IMenuApi.AddMenuItem(string parentMenu, string text, Action onClick, string? shortcut)
    {
        _form.Invoke(() =>
        {
            ToolStripMenuItem? parent = FindOrCreateTopLevelMenu(parentMenu);
            var item = new ToolStripMenuItem(text);
            item.Click += (_, _) => onClick();

            if (shortcut is not null)
                item.ShortcutKeyDisplayString = shortcut;

            item.Name = $"plugin_{parentMenu}_{text}";
            parent.DropDownItems.Add(item);
        });
    }

    void IMenuApi.AddSeparator(string parentMenu)
    {
        _form.Invoke(() =>
        {
            ToolStripMenuItem? parent = FindOrCreateTopLevelMenu(parentMenu);
            parent.DropDownItems.Add(new ToolStripSeparator());
        });
    }

    void IMenuApi.RemoveMenuItem(string id)
    {
        _form.Invoke(() =>
        {
            foreach (ToolStripItem topItem in _form.MainMenu.Items)
            {
                if (topItem is ToolStripMenuItem topMenu)
                {
                    for (int i = topMenu.DropDownItems.Count - 1; i >= 0; i--)
                    {
                        if (topMenu.DropDownItems[i].Name == id)
                        {
                            topMenu.DropDownItems.RemoveAt(i);
                            return;
                        }
                    }
                }
            }
        });
    }

    // ── IPanelApi ────────────────────────────────────────────────────

    private readonly Dictionary<string, Control> _panels = new(StringComparer.OrdinalIgnoreCase);

    void IPanelApi.RegisterPanel(string id, string title, object control)
    {
        if (control is not Control winControl)
            throw new ArgumentException("Control must be a System.Windows.Forms.Control.", nameof(control));

        _panels[id] = winControl;
    }

    void IPanelApi.ShowPanel(string id)
    {
        _form.Invoke(() =>
        {
            if (_panels.TryGetValue(id, out Control? control))
            {
                // Add to side panel or bottom panel depending on context.
                if (!_form.SidePanel.Controls.Contains(control))
                {
                    control.Dock = DockStyle.Fill;
                    _form.SidePanel.Controls.Add(control);
                }
                control.Visible = true;
                _form.IsSidePanelVisible = true;
            }
        });
    }

    void IPanelApi.HidePanel(string id)
    {
        _form.Invoke(() =>
        {
            if (_panels.TryGetValue(id, out Control? control))
                control.Visible = false;
        });
    }

    void IPanelApi.RemovePanel(string id)
    {
        _form.Invoke(() =>
        {
            if (_panels.TryGetValue(id, out Control? control))
            {
                _form.SidePanel.Controls.Remove(control);
                _form.BottomPanel.Controls.Remove(control);
                control.Dispose();
                _panels.Remove(id);
            }
        });
    }

    // ── IBufferApi ───────────────────────────────────────────────────

    private Bascanka.Core.Buffer.PieceTable GetActiveBuffer()
    {
        var tab = _form.ActiveTab ?? throw new InvalidOperationException(Strings.ErrorNoDocumentOpen);
        string text = tab.Editor.GetAllText();
        // Create a temporary PieceTable for API access.
        // In a full implementation, this would reference the editor's live buffer.
        return new Bascanka.Core.Buffer.PieceTable(text);
    }

    string IBufferApi.GetText(long offset, long length) => GetActiveBuffer().GetText(offset, length);
    void IBufferApi.Insert(long offset, string text) => GetActiveBuffer().Insert(offset, text);
    void IBufferApi.Delete(long offset, long length) => GetActiveBuffer().Delete(offset, length);

    void IBufferApi.Replace(long offset, long length, string newText)
    {
        var buf = GetActiveBuffer();
        buf.Delete(offset, length);
        buf.Insert(offset, newText);
    }

    long IBufferApi.Length => _form.ActiveTab?.Editor.GetBufferLength() ?? 0;

    long IBufferApi.LineCount
    {
        get
        {
            if (_form.ActiveTab is null) return 0;
            return GetActiveBuffer().LineCount;
        }
    }

    string IBufferApi.GetLine(long lineIndex) => GetActiveBuffer().GetLine(lineIndex);

    (long Line, long Column) IBufferApi.OffsetToLineColumn(long offset)
    {
        // Simplified: walk the text to compute line/column.
        var buf = GetActiveBuffer();
        if (offset <= 0) return (0, 0);
        string text = buf.GetText(0, Math.Min(offset, buf.Length));
        long line = 0, col = 0;
        foreach (char c in text)
        {
            if (c == '\n') { line++; col = 0; }
            else col++;
        }
        return (line, col);
    }

    long IBufferApi.LineColumnToOffset(long line, long column)
    {
        var buf = GetActiveBuffer();
        if (line == 0) return Math.Min(column, buf.Length);
        long offset = buf.GetLineStartOffset(line);
        return Math.Min(offset + column, buf.Length);
    }

    // ── IDocumentApi ─────────────────────────────────────────────────

    void IDocumentApi.OpenFile(string path)
    {
        _form.Invoke(() => _form.OpenFile(path));
    }

    void IDocumentApi.NewDocument()
    {
        _form.Invoke(() => _form.NewDocument());
    }

    void IDocumentApi.SaveActiveDocument()
    {
        _form.Invoke(() => _form.SaveCurrentDocument());
    }

    void IDocumentApi.SaveActiveDocumentAs(string path)
    {
        // Set the file path and save.
        _form.Invoke(() =>
        {
            var tab = _form.ActiveTab;
            if (tab is null) return;
            tab.FilePath = path;
            tab.Title = Path.GetFileName(path);
            _form.SaveCurrentDocument();
        });
    }

    string? IDocumentApi.ActiveDocumentPath => _form.ActiveTab?.FilePath;

    IReadOnlyList<string> IDocumentApi.OpenDocumentPaths =>
        _form.Tabs.Select(t => t.FilePath ?? string.Empty).ToList().AsReadOnly();

    void IDocumentApi.ActivateDocument(int index)
    {
        _form.Invoke(() => _form.ActivateTab(index));
    }

    // ── IStatusBarApi ────────────────────────────────────────────────

    void IStatusBarApi.SetField(string id, string text)
    {
        _form.Invoke(() => _form.StatusBarManager.SetPluginField(id, text));
    }

    void IStatusBarApi.RemoveField(string id)
    {
        _form.Invoke(() => _form.StatusBarManager.RemovePluginField(id));
    }

    // ── Plugin loading ───────────────────────────────────────────────

    /// <summary>
    /// Gets the names of all currently loaded plugins.
    /// </summary>
    public IReadOnlyList<string> LoadedPluginNames =>
        _plugins.Select(p => p.Plugin.Name).ToList().AsReadOnly();

    /// <summary>
    /// Scans the plugins directory and loads all plugin DLLs and .csx scripts.
    /// </summary>
    public void LoadPlugins()
    {
        if (!Directory.Exists(PluginsDirectory))
            return;

        // Load DLL plugins.
        foreach (string dllPath in Directory.GetFiles(PluginsDirectory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                LoadPluginAssembly(dllPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load plugin {dllPath}: {ex.Message}");
            }
        }

        // Load .csx script plugins.
        foreach (string csxPath in Directory.GetFiles(PluginsDirectory, "*.csx", SearchOption.AllDirectories))
        {
            try
            {
                Assembly? asm = ScriptCompiler.Compile(csxPath);
                if (asm is not null)
                    InitializePluginsFromAssembly(asm, csxPath, loadContext: null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to compile script plugin {csxPath}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Loads a single plugin assembly from the given DLL path.
    /// </summary>
    private void LoadPluginAssembly(string dllPath)
    {
        var context = new PluginLoadContext(dllPath);
        Assembly assembly = context.LoadFromAssemblyPath(dllPath);
        InitializePluginsFromAssembly(assembly, dllPath, context);
    }

    /// <summary>
    /// Discovers and initializes all <see cref="IPlugin"/> implementations in the assembly.
    /// </summary>
    private void InitializePluginsFromAssembly(Assembly assembly, string sourcePath, AssemblyLoadContext? loadContext)
    {
        foreach (Type type in assembly.GetTypes())
        {
            if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                continue;

            if (Activator.CreateInstance(type) is not IPlugin plugin)
                continue;

            try
            {
                plugin.Initialize(this);

                _plugins.Add(new LoadedPlugin
                {
                    Plugin = plugin,
                    SourcePath = sourcePath,
                    LoadContext = loadContext,
                });

                System.Diagnostics.Debug.WriteLine(
                    $"Loaded plugin: {plugin.Name} v{plugin.Version} by {plugin.Author}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Plugin {type.FullName} failed to initialize: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Unloads a plugin by name and releases its <see cref="AssemblyLoadContext"/>.
    /// </summary>
    public void UnloadPlugin(string name)
    {
        for (int i = _plugins.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_plugins[i].Plugin.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                var entry = _plugins[i];

                try { entry.Plugin.Shutdown(); }
                catch { /* ignore shutdown errors */ }

                _plugins.RemoveAt(i);

                // Unload the assembly load context if it's collectible.
                if (entry.LoadContext is PluginLoadContext plc)
                {
                    plc.Unload();
                }
            }
        }
    }

    /// <summary>
    /// Shuts down all loaded plugins.
    /// </summary>
    public void Shutdown()
    {
        foreach (var entry in _plugins)
        {
            try { entry.Plugin.Shutdown(); }
            catch { /* ignore */ }

            if (entry.LoadContext is PluginLoadContext plc)
            {
                try { plc.Unload(); }
                catch { /* ignore */ }
            }
        }
        _plugins.Clear();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private ToolStripMenuItem FindOrCreateTopLevelMenu(string menuName)
    {
        foreach (ToolStripItem item in _form.MainMenu.Items)
        {
            if (item is ToolStripMenuItem topMenu &&
                string.Equals(topMenu.Text, menuName, StringComparison.OrdinalIgnoreCase))
            {
                return topMenu;
            }
        }

        // Create a new top-level menu.
        var newMenu = new ToolStripMenuItem(menuName);
        // Insert before Help (last item).
        int insertIndex = Math.Max(0, _form.MainMenu.Items.Count - 1);
        _form.MainMenu.Items.Insert(insertIndex, newMenu);
        return newMenu;
    }
}
