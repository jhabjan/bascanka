using System.Text.Json;
using Microsoft.Win32;

namespace Bascanka.App;

/// <summary>
/// Manages application settings using the Windows Registry.
/// All values are stored under <c>HKEY_CURRENT_USER\Software\Bascanka</c>.
/// </summary>
internal static class SettingsManager
{
    private const string RegistryKeyPath = @"Software\Bascanka";

    /// <summary>Gets a string value from the registry, or the default if not found.</summary>
    public static string GetString(string name, string defaultValue = "")
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            return key?.GetValue(name) as string ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>Sets a string value in the registry.</summary>
    public static void SetString(string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(name, value, RegistryValueKind.String);
        }
        catch
        {
            // Silently ignore if registry access fails.
        }
    }

    /// <summary>Gets a boolean value from the registry.</summary>
    public static bool GetBool(string name, bool defaultValue = false)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(name) is int intVal)
                return intVal != 0;
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>Sets a boolean value in the registry (stored as DWORD 0/1).</summary>
    public static void SetBool(string name, bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            // Silently ignore.
        }
    }

    /// <summary>Gets an integer value from the registry.</summary>
    public static int GetInt(string name, int defaultValue = 0)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(name) is int intVal)
                return intVal;
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>Sets an integer value in the registry.</summary>
    public static void SetInt(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch
        {
            // Silently ignore.
        }
    }

    // ── Well-known setting names ────────────────────────────────────

    public const string KeyLanguage = "Language";
    public const string KeyWordWrap = "WordWrap";
    public const string KeyShowWhitespace = "ShowWhitespace";
    public const string KeyShowLineNumbers = "ShowLineNumbers";
    public const string KeyTheme = "Theme";

    // Editor
    public const string KeyFontFamily = "FontFamily";
    public const string KeyFontSize = "FontSize";
    public const string KeyTabWidth = "TabWidth";
    public const string KeyScrollSpeed = "ScrollSpeed";

    // Display
    public const string KeyCaretBlinkRate = "CaretBlinkRate";
    public const string KeyMaxTabWidth = "MaxTabWidth";

    // Editor (continued)
    public const string KeyAutoIndent = "AutoIndent";
    public const string KeyCaretScrollBuffer = "CaretScrollBuffer";

    // Display (continued)
    public const string KeyTextLeftPadding = "TextLeftPadding";
    public const string KeyLineSpacing = "LineSpacing";
    public const string KeyMinZoomFontSize = "MinZoomFontSize";
    public const string KeyWhitespaceOpacity = "WhitespaceOpacity";
    public const string KeyFoldIndicatorOpacity = "FoldIndicatorOpacity";
    public const string KeyGutterPaddingLeft = "GutterPaddingLeft";
    public const string KeyGutterPaddingRight = "GutterPaddingRight";
    public const string KeyFoldButtonSize = "FoldButtonSize";
    public const string KeyBookmarkSize = "BookmarkSize";
    public const string KeyTabHeight = "TabHeight";
    public const string KeyMinTabWidth = "MinTabWidth";

    // Theme overrides
    public const string KeyThemeOverridesPrefix = "ThemeOverrides.";

    // Dialog sizes
    public const string KeySettingsWidth = "SettingsFormWidth";
    public const string KeySettingsHeight = "SettingsFormHeight";
    public const string KeyHighlightDlgWidth = "HighlightDlgWidth";
    public const string KeyHighlightDlgHeight = "HighlightDlgHeight";

    // Performance
    public const string KeyLargeFileThresholdMB = "LargeFileThresholdMB";
    public const string KeyFoldingMaxFileSizeMB = "FoldingMaxFileSizeMB";
    public const string KeyMaxRecentFiles = "MaxRecentFiles";
    public const string KeySearchHistoryLimit = "SearchHistoryLimit";
    public const string KeySearchDebounce = "SearchDebounce";

    /// <summary>
    /// Deletes all values under the main Bascanka registry key,
    /// preserving the Session sub-key.
    /// </summary>
    public static void ResetToDefaults()
    {
        try
        {
            // Save session state before clearing.
            using var sessionSrc = Registry.CurrentUser.OpenSubKey(SessionKeyPath);
            var sessionValues = new List<(string name, object value, RegistryValueKind kind)>();
            if (sessionSrc is not null)
            {
                foreach (string name in sessionSrc.GetValueNames())
                {
                    var val = sessionSrc.GetValue(name);
                    var kind = sessionSrc.GetValueKind(name);
                    if (val is not null)
                        sessionValues.Add((name, val, kind));
                }
            }

            // Delete and recreate the main key.
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
            Registry.CurrentUser.CreateSubKey(RegistryKeyPath);

            // Restore session state.
            if (sessionValues.Count > 0)
            {
                using var sessionDst = Registry.CurrentUser.CreateSubKey(SessionKeyPath);
                foreach (var (name, val, kind) in sessionValues)
                    sessionDst.SetValue(name, val, kind);
            }
        }
        catch { }
    }

    // ── Session sub-key ───────────────────────────────────────────

    private const string SessionKeyPath = @"Software\Bascanka\Session";

    /// <summary>Gets an integer from the Session sub-key.</summary>
    public static int GetSessionInt(string name, int defaultValue = 0)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SessionKeyPath);
            if (key?.GetValue(name) is int intVal)
                return intVal;
            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>Sets an integer in the Session sub-key.</summary>
    public static void SetSessionInt(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SessionKeyPath);
            key.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch { }
    }

    /// <summary>Gets a string from the Session sub-key.</summary>
    public static string GetSessionString(string name, string defaultValue = "")
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SessionKeyPath);
            return key?.GetValue(name) as string ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>Sets a string in the Session sub-key.</summary>
    public static void SetSessionString(string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SessionKeyPath);
            key.SetValue(name, value, RegistryValueKind.String);
        }
        catch { }
    }

    /// <summary>Deletes the entire Session sub-key tree, clearing all session state.</summary>
    public static void ClearSessionState()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(SessionKeyPath, throwOnMissingSubKey: false);
        }
        catch { }
    }

    // ── Explorer context menu ───────────────────────────────────────

    private const string ExplorerContextKeyPath = @"*\shell\Bascanka";
    private const string ExplorerCommandKeyPath = @"*\shell\Bascanka\command";

    /// <summary>
    /// Returns whether the "Edit with Bascanka" context menu entry is registered.
    /// </summary>
    public static bool IsExplorerContextMenuRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\" + ExplorerContextKeyPath);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers "Edit with Bascanka" in the Windows Explorer right-click
    /// context menu for all file types. Uses HKCU so no admin rights needed.
    /// </summary>
    public static void RegisterExplorerContextMenu()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;

            using var shellKey = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\" + ExplorerContextKeyPath);
            shellKey.SetValue("", "Edit with Bascanka");
            shellKey.SetValue("Icon", $"\"{exePath}\",0");

            using var cmdKey = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\" + ExplorerCommandKeyPath);
            cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch
        {
            // Silently ignore.
        }
    }

    /// <summary>
    /// Removes the "Edit with Bascanka" context menu entry from Explorer.
    /// </summary>
    public static void UnregisterExplorerContextMenu()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Classes\" + ExplorerContextKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // Silently ignore.
        }
    }

    // ── Theme overrides ─────────────────────────────────────────────

    /// <summary>
    /// Gets the JSON string of colour overrides for the specified theme, or null if none.
    /// </summary>
    public static string? GetThemeOverrides(string themeName)
    {
        string val = GetString(KeyThemeOverridesPrefix + themeName);
        return string.IsNullOrEmpty(val) ? null : val;
    }

    /// <summary>
    /// Stores (or clears) the JSON colour overrides for the specified theme.
    /// </summary>
    public static void SetThemeOverrides(string themeName, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            // Remove the value.
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
                key?.DeleteValue(KeyThemeOverridesPrefix + themeName, throwOnMissingValue: false);
            }
            catch { }
        }
        else
        {
            SetString(KeyThemeOverridesPrefix + themeName, json);
        }
    }

    // ── Export / Import ─────────────────────────────────────────────

    /// <summary>
    /// Well-known setting keys (non-session, non-theme-override) for export/import.
    /// </summary>
    private static readonly string[] ExportableKeys =
    [
        KeyLanguage, KeyTheme,
        KeyFontFamily, KeyFontSize, KeyTabWidth, KeyScrollSpeed,
        KeyAutoIndent, KeyCaretScrollBuffer,
        KeyWordWrap, KeyShowWhitespace, KeyShowLineNumbers,
        KeyCaretBlinkRate, KeyMaxTabWidth,
        KeyTextLeftPadding, KeyLineSpacing, KeyMinZoomFontSize,
        KeyWhitespaceOpacity, KeyFoldIndicatorOpacity,
        KeyGutterPaddingLeft, KeyGutterPaddingRight,
        KeyFoldButtonSize, KeyBookmarkSize,
        KeyTabHeight, KeyMinTabWidth,
        KeyLargeFileThresholdMB, KeyFoldingMaxFileSizeMB,
        KeyMaxRecentFiles, KeySearchHistoryLimit, KeySearchDebounce,
    ];

    /// <summary>
    /// Serializes all settings and theme overrides to a JSON string.
    /// </summary>
    public static string ExportToJson()
    {
        var settings = new Dictionary<string, object>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key is not null)
            {
                foreach (string name in ExportableKeys)
                {
                    var val = key.GetValue(name);
                    if (val is not null)
                        settings[name] = val;
                }
            }
        }
        catch { }

        var themeOverrides = new Dictionary<string, string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key is not null)
            {
                foreach (string name in key.GetValueNames())
                {
                    if (name.StartsWith(KeyThemeOverridesPrefix, StringComparison.Ordinal))
                    {
                        string themeName = name[KeyThemeOverridesPrefix.Length..];
                        if (key.GetValue(name) is string json)
                            themeOverrides[themeName] = json;
                    }
                }
            }
        }
        catch { }

        var root = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["settings"] = settings,
            ["themeOverrides"] = themeOverrides,
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Imports settings from a JSON string previously produced by <see cref="ExportToJson"/>.
    /// </summary>
    public static void ImportFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("settings", out var settingsEl))
        {
            foreach (var prop in settingsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    SetString(prop.Name, prop.Value.GetString()!);
                else if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int intVal))
                    SetInt(prop.Name, intVal);
            }
        }

        if (root.TryGetProperty("themeOverrides", out var overridesEl))
        {
            foreach (var prop in overridesEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    SetThemeOverrides(prop.Name, prop.Value.GetString());
            }
        }
    }

}
