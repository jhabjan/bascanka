using System.Text.Json;

namespace Bascanka.App;

/// <summary>
/// Manages keyboard shortcuts for the editor. Provides a registry of
/// command-name-to-key-binding mappings with customization support.
/// Shortcut overrides are persisted to a JSON file.
/// </summary>
public sealed class KeyboardShortcutManager
{
    private static readonly string SettingsDirectory = SettingsManager.AppDataFolder;

    private static readonly string ShortcutFilePath =
        Path.Combine(SettingsDirectory, "shortcuts.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Dictionary<string, ShortcutBinding> _bindings = new(StringComparer.OrdinalIgnoreCase);

    public KeyboardShortcutManager()
    {
        LoadCustomBindings();
    }

    /// <summary>
    /// Registers a keyboard shortcut for a named command.
    /// If a custom binding exists for this command (loaded from settings),
    /// the custom binding takes precedence over the provided defaults.
    /// </summary>
    public void RegisterShortcut(string commandName, Keys key, bool ctrl, bool shift, bool alt, Action handler)
    {
        if (_bindings.TryGetValue(commandName, out ShortcutBinding? existing))
        {
            // Custom binding loaded from settings -- update the handler only.
            existing.Handler = handler;
            return;
        }

        _bindings[commandName] = new ShortcutBinding
        {
            CommandName = commandName,
            Key = key,
            Ctrl = ctrl,
            Shift = shift,
            Alt = alt,
            Handler = handler,
        };
    }

    /// <summary>
    /// Processes a key combination. Returns true if a matching shortcut was found and executed.
    /// </summary>
    public bool ProcessShortcut(Keys keyCode, bool ctrl, bool shift, bool alt)
    {
        // Strip modifier bits from keyCode if present.
        Keys baseKey = keyCode & Keys.KeyCode;

        foreach (var binding in _bindings.Values)
        {
            if (binding.Key == baseKey &&
                binding.Ctrl == ctrl &&
                binding.Shift == shift &&
                binding.Alt == alt &&
                binding.Handler is not null)
            {
                binding.Handler.Invoke();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a human-readable shortcut text for the given command name.
    /// Example: "Ctrl+S", "Ctrl+Shift+P", "F5".
    /// </summary>
    public string GetShortcutText(string commandName)
    {
        if (!_bindings.TryGetValue(commandName, out ShortcutBinding? binding))
            return string.Empty;

        var parts = new List<string>();
        if (binding.Ctrl) parts.Add("Ctrl");
        if (binding.Shift) parts.Add("Shift");
        if (binding.Alt) parts.Add("Alt");
        parts.Add(FormatKeyName(binding.Key));

        return string.Join("+", parts);
    }

    /// <summary>
    /// Updates the key binding for a command and saves the customization to disk.
    /// </summary>
    public void SetShortcut(string commandName, Keys key, bool ctrl, bool shift, bool alt)
    {
        if (_bindings.TryGetValue(commandName, out ShortcutBinding? binding))
        {
            binding.Key = key;
            binding.Ctrl = ctrl;
            binding.Shift = shift;
            binding.Alt = alt;
        }
        else
        {
            _bindings[commandName] = new ShortcutBinding
            {
                CommandName = commandName,
                Key = key,
                Ctrl = ctrl,
                Shift = shift,
                Alt = alt,
            };
        }

        SaveCustomBindings();
    }

    /// <summary>
    /// Returns all registered command names and their shortcut texts.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllShortcuts()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _bindings)
        {
            result[kvp.Key] = GetShortcutText(kvp.Key);
        }
        return result;
    }

    // ── Persistence ──────────────────────────────────────────────────

    private void SaveCustomBindings()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            var data = new Dictionary<string, ShortcutData>();
            foreach (var kvp in _bindings)
            {
                data[kvp.Key] = new ShortcutData
                {
                    Key = (int)kvp.Value.Key,
                    Ctrl = kvp.Value.Ctrl,
                    Shift = kvp.Value.Shift,
                    Alt = kvp.Value.Alt,
                };
            }

            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(ShortcutFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save shortcuts: {ex.Message}");
        }
    }

    private void LoadCustomBindings()
    {
        try
        {
            if (!File.Exists(ShortcutFilePath))
                return;

            string json = File.ReadAllText(ShortcutFilePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, ShortcutData>>(json, JsonOptions);

            if (data is null) return;

            foreach (var kvp in data)
            {
                _bindings[kvp.Key] = new ShortcutBinding
                {
                    CommandName = kvp.Key,
                    Key = (Keys)kvp.Value.Key,
                    Ctrl = kvp.Value.Ctrl,
                    Shift = kvp.Value.Shift,
                    Alt = kvp.Value.Alt,
                    Handler = null, // Will be set when RegisterShortcut is called.
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load shortcuts: {ex.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string FormatKeyName(Keys key)
    {
        return key switch
        {
            Keys.OemMinus => "-",
            Keys.Oemplus => "+",
            Keys.Oem5 => "\\",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.Oem2 => "/",
            Keys.Oemtilde => "`",
            Keys.D0 => "0",
            Keys.D1 => "1",
            Keys.D2 => "2",
            Keys.D3 => "3",
            Keys.D4 => "4",
            Keys.D5 => "5",
            Keys.D6 => "6",
            Keys.D7 => "7",
            Keys.D8 => "8",
            Keys.D9 => "9",
            Keys.Back => "Backspace",
            Keys.Return => "Enter",
            Keys.Escape => "Esc",
            Keys.Space => "Space",
            Keys.Tab => "Tab",
            _ => key.ToString(),
        };
    }
}
