using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bascanka.Editor.Macros;

/// <summary>
/// Manages a collection of saved <see cref="Macro"/> instances.  Provides
/// JSON serialization to persist macros to disk, and supports assigning
/// keyboard shortcuts to individual macros.
/// </summary>
public sealed class MacroManager
{
    // ── Serialization options ───────────────────────────────────────────

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // ── Fields ──────────────────────────────────────────────────────────

    private readonly List<Macro> _macros = [];

    // ── Events ──────────────────────────────────────────────────────────

    /// <summary>Raised when the macro list changes (add, remove, load).</summary>
    public event EventHandler? MacrosChanged;

    // ── Properties ──────────────────────────────────────────────────────

    /// <summary>All macros currently managed by this instance.</summary>
    public IReadOnlyList<Macro> Macros => _macros.AsReadOnly();

    // ── Collection management ───────────────────────────────────────────

    /// <summary>Adds a macro to the managed collection.</summary>
    public void Add(Macro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        _macros.Add(macro);
        MacrosChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes a macro from the managed collection.</summary>
    /// <returns><see langword="true"/> if the macro was found and removed.</returns>
    public bool Remove(Macro macro)
    {
        bool removed = _macros.Remove(macro);
        if (removed)
            MacrosChanged?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <summary>Removes all macros from the collection.</summary>
    public void Clear()
    {
        if (_macros.Count == 0) return;
        _macros.Clear();
        MacrosChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Finds a macro by name (case-insensitive).
    /// </summary>
    public Macro? FindByName(string name) =>
        _macros.Find(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds the macro bound to the given shortcut string.
    /// </summary>
    public Macro? FindByShortcut(string shortcutKey) =>
        _macros.Find(m => string.Equals(m.ShortcutKey, shortcutKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Assigns a keyboard shortcut to a macro.  If another macro already uses
    /// the same shortcut, the old binding is cleared.
    /// </summary>
    /// <param name="macro">The macro to bind.</param>
    /// <param name="shortcutKey">
    /// The shortcut string (e.g. <c>"Ctrl+Shift+1"</c>), or <see langword="null"/>
    /// to clear the binding.
    /// </param>
    public void AssignShortcut(Macro macro, string? shortcutKey)
    {
        ArgumentNullException.ThrowIfNull(macro);

        // Clear the shortcut from any other macro that has it.
        if (shortcutKey is not null)
        {
            foreach (Macro m in _macros)
            {
                if (m != macro && string.Equals(m.ShortcutKey, shortcutKey, StringComparison.OrdinalIgnoreCase))
                    m.ShortcutKey = null;
            }
        }

        macro.ShortcutKey = shortcutKey;
        MacrosChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Single-file serialization ───────────────────────────────────────

    /// <summary>
    /// Serializes a single macro to a JSON file.
    /// </summary>
    /// <param name="macro">The macro to save.</param>
    /// <param name="path">The destination file path.</param>
    public static void SaveToFile(Macro macro, string path)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(macro, s_jsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Deserializes a macro from a JSON file.
    /// </summary>
    /// <param name="path">The source file path.</param>
    /// <returns>The deserialized <see cref="Macro"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the file cannot be deserialized into a <see cref="Macro"/>.
    /// </exception>
    public static Macro LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);
        Macro? macro = JsonSerializer.Deserialize<Macro>(json, s_jsonOptions);

        return macro ?? throw new InvalidOperationException(
            $"Failed to deserialize macro from '{path}'.");
    }

    // ── Bulk serialization ──────────────────────────────────────────────

    /// <summary>
    /// Saves every macro in the collection to individual JSON files inside
    /// the specified directory.  File names are derived from the macro name.
    /// </summary>
    /// <param name="directory">The target directory.</param>
    public void SaveAll(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);

        // Remove old macro files so that deleted macros do not persist.
        foreach (string existing in Directory.GetFiles(directory, "*.macro.json"))
        {
            try { File.Delete(existing); }
            catch { /* best effort */ }
        }

        for (int i = 0; i < _macros.Count; i++)
        {
            Macro macro = _macros[i];
            string safeFileName = SanitizeFileName(macro.Name);
            string filePath = Path.Combine(directory, $"{i:D4}_{safeFileName}.macro.json");
            SaveToFile(macro, filePath);
        }
    }

    /// <summary>
    /// Loads all <c>*.macro.json</c> files from the specified directory,
    /// replacing the current collection.
    /// </summary>
    /// <param name="directory">The source directory.</param>
    public void LoadAll(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _macros.Clear();

        if (!Directory.Exists(directory))
        {
            MacrosChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        string[] files = Directory.GetFiles(directory, "*.macro.json");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            try
            {
                Macro macro = LoadFromFile(file);
                _macros.Add(macro);
            }
            catch
            {
                // Skip files that cannot be deserialized.
            }
        }

        MacrosChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces characters that are invalid in file names with underscores.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        string sanitized = new string(chars).Trim();
        return sanitized.Length == 0 ? "macro" : sanitized;
    }
}
