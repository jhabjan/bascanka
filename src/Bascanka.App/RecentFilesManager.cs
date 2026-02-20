using System.Text.Json;

namespace Bascanka.App;

/// <summary>
/// Manages the most-recently-used (MRU) file list. Persists up to
/// <see cref="MaxRecentFiles"/> entries to a JSON file in the user's
/// AppData folder. Always reads from disk so multiple app instances
/// share the same list.
/// </summary>
public sealed class RecentFilesManager
{
    /// <summary>Maximum number of recent files to retain.</summary>
    public static int MaxRecentFiles { get; set; } = 20;

    private static readonly string DataDirectory = SettingsManager.AppDataFolder;

    private static readonly string RecentFilePath =
        Path.Combine(DataDirectory, "recent.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Adds a file path to the top of the recent files list.
    /// If the path already exists, it is moved to the top.
    /// The list is truncated to <see cref="MaxRecentFiles"/> entries.
    /// Reloads from disk first to merge changes from other instances.
    /// </summary>
    public void AddFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        string fullPath = Path.GetFullPath(path);

        // Reload from disk to incorporate changes from other instances.
        var files = Load();

        // Remove if already present (to move to top).
        files.RemoveAll(f =>
            string.Equals(f, fullPath, StringComparison.OrdinalIgnoreCase));

        // Insert at the beginning.
        files.Insert(0, fullPath);

        // Trim to maximum.
        while (files.Count > MaxRecentFiles)
            files.RemoveAt(files.Count - 1);

        Save(files);
    }

    /// <summary>
    /// Returns the list of recent file paths, most recent first.
    /// Always reloads from disk so changes from other instances are visible.
    /// </summary>
    public IReadOnlyList<string> GetRecentFiles()
    {
        return Load().AsReadOnly();
    }

    /// <summary>
    /// Clears the entire recent files list and deletes the persisted file.
    /// </summary>
    public void ClearRecentFiles()
    {
        Save(new List<string>());
    }

    // ── Persistence ──────────────────────────────────────────────────

    private static void Save(List<string> files)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string json = JsonSerializer.Serialize(files, JsonOptions);

            // Atomic write: write to .tmp then rename, to avoid corruption
            // if another instance reads concurrently.
            string tmpPath = RecentFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, RecentFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save recent files: {ex.Message}");
        }
    }

    private static List<string> Load()
    {
        try
        {
            if (!File.Exists(RecentFilePath))
                return new List<string>();

            string json = File.ReadAllText(RecentFilePath);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list ?? new List<string>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load recent files: {ex.Message}");
            return new List<string>();
        }
    }
}
