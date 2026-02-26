using Bascanka.Editor.Tabs;

namespace Bascanka.App;

/// <summary>
/// Monitors open files for external changes (modifications or deletions).
/// Creates one <see cref="FileSystemWatcher"/> per watched file and debounces
/// rapid change notifications.
/// </summary>
public sealed class FileWatcher(MainForm form) : IDisposable
{
    /// <summary>Debounce interval to coalesce rapid file system events.</summary>
    private const int DebounceMilliseconds = 300;

    private readonly MainForm _form = form;
    private readonly Dictionary<string, WatchEntry> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _suppressedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _ignoreAll;
    private bool _disposed;

	/// <summary>
	/// Starts watching the file associated with the given tab.
	/// </summary>
	public void Watch(TabInfo tab)
    {
        if (tab.FilePath is null) return;

        string path = Path.GetFullPath(tab.FilePath);

        // Don't create a duplicate watcher.
        if (_watchers.ContainsKey(path))
            return;

        string? directory = Path.GetDirectoryName(path);
        string fileName = Path.GetFileName(path);

        if (directory is null) return;

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        var entry = new WatchEntry
        {
            Watcher = watcher,
            FilePath = path,
            DebounceTimer = new System.Windows.Forms.Timer { Interval = DebounceMilliseconds },
            PendingChange = false,
        };

        entry.DebounceTimer.Tick += (_, _) =>
        {
            entry.DebounceTimer.Stop();
            if (entry.PendingChange)
            {
                entry.PendingChange = false;
                HandleFileChanged(path);
            }
        };

        watcher.Changed += (_, args) =>
        {
            if (_disposed) return;
            // Schedule on UI thread.
            _form.BeginInvoke(() =>
            {
                if (_suppressedPaths.TryGetValue(path, out var suppressTime)
                    && (DateTime.UtcNow - suppressTime).TotalMilliseconds < 1000)
                    return;

                _suppressedPaths.Remove(path);
                entry.PendingChange = true;
                entry.DebounceTimer.Stop();
                entry.DebounceTimer.Start();
            });
        };

        watcher.Deleted += (_, args) =>
        {
            if (_disposed) return;
            _form.BeginInvoke(() =>
            {
                if (_suppressedPaths.TryGetValue(path, out var suppressTime)
                    && (DateTime.UtcNow - suppressTime).TotalMilliseconds < 2000)
                    return;

                HandleFileDeleted(path);
            });
        };

        watcher.Renamed += (_, args) =>
        {
            if (_disposed) return;
            _form.BeginInvoke(() =>
            {
                if (_suppressedPaths.TryGetValue(path, out var suppressTime)
                    && (DateTime.UtcNow - suppressTime).TotalMilliseconds < 2000)
                    return;

                HandleFileDeleted(path);
            });
        };

        _watchers[path] = entry;
    }

    /// <summary>
    /// Stops watching the specified file.
    /// </summary>
    public void Unwatch(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (_watchers.TryGetValue(fullPath, out WatchEntry? entry))
        {
            entry.DebounceTimer.Stop();
            entry.DebounceTimer.Dispose();
            entry.Watcher.EnableRaisingEvents = false;
            entry.Watcher.Dispose();
            _watchers.Remove(fullPath);
        }
    }

    /// <summary>
    /// Suppresses the next change notification for a specific path.
    /// Used when the editor itself is saving the file.
    /// </summary>
    public void SuppressNextChange(string path)
    {
        _suppressedPaths[Path.GetFullPath(path)] = DateTime.UtcNow;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var entry in _watchers.Values)
        {
            entry.DebounceTimer.Stop();
            entry.DebounceTimer.Dispose();
            entry.Watcher.EnableRaisingEvents = false;
            entry.Watcher.Dispose();
        }

        _watchers.Clear();
    }

    // ── Handlers ─────────────────────────────────────────────────────

    private void HandleFileChanged(string path)
    {
        if (_ignoreAll) return;

        DialogResult result = MessageBox.Show(
            _form,
            string.Format(Strings.FileModifiedExternally, Path.GetFileName(path)),
            Strings.AppTitle,
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        switch (result)
        {
            case DialogResult.Yes:
                // Reload.
                ReloadFile(path);
                break;

            case DialogResult.Cancel:
                // Ignore All future notifications.
                _ignoreAll = true;
                break;

            // DialogResult.No: Ignore this one.
        }
    }

    private void HandleFileDeleted(string path)
    {
        MessageBox.Show(
            _form,
            string.Format(Strings.FileDeletedExternally, Path.GetFileName(path)),
            Strings.AppTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void ReloadFile(string path)
    {
        // Find the tab with this file and reload it.
        foreach (var tab in _form.Tabs)
        {
            if (string.Equals(tab.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                int index = _form.Tabs.ToList().IndexOf(tab);
                if (index >= 0)
                {
                    _form.ActivateTab(index);
                    _form.ReloadActiveDocument();
                }
                break;
            }
        }
    }
}
