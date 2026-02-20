using System.Text.Json;
using Microsoft.Win32;

namespace Bascanka.App;

/// <summary>
/// Manages application settings using JSON files in %AppData%\Bascanka.
/// Settings are stored in <c>settings.json</c>, session state in <c>session.json</c>.
/// Explorer context menu registration remains in the Windows Registry.
/// </summary>
internal static class SettingsManager
{
    // ── File paths ──────────────────────────────────────────────────

#if DEBUG
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bascanka", "debug");
#else
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bascanka");
#endif

    private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");
    private static readonly string SessionFilePath = Path.Combine(AppDataFolder, "session.json");

    // ── In-memory caches ────────────────────────────────────────────

    private static Dictionary<string, JsonElement>? _settingsCache;
    private static Dictionary<string, JsonElement>? _sessionCache;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // ── Lazy loading ────────────────────────────────────────────────

    private static void EnsureLoaded()
    {
        if (_settingsCache is not null) return;

        Directory.CreateDirectory(AppDataFolder);

        // One-time migration from registry.
        if (!File.Exists(SettingsFilePath))
            MigrateFromRegistry();

        _settingsCache = LoadJsonFile(SettingsFilePath);
    }

    private static void EnsureSessionLoaded()
    {
        if (_sessionCache is not null) return;

        Directory.CreateDirectory(AppDataFolder);

        // Migration populates both caches, but only if settings.json didn't exist.
        // If session.json is missing but settings.json exists, just load empty.
        _sessionCache = LoadJsonFile(SessionFilePath);
    }

    private static Dictionary<string, JsonElement> LoadJsonFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var dict = new Dictionary<string, JsonElement>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                    dict[prop.Name] = prop.Value.Clone();
                return dict;
            }
        }
        catch { }
        return new Dictionary<string, JsonElement>();
    }

    // ── Atomic save ─────────────────────────────────────────────────

    private static void SaveSettings()
    {
        if (_settingsCache is null) return;
        SaveJsonFile(SettingsFilePath, _settingsCache);
    }

    private static void SaveSession()
    {
        if (_sessionCache is null) return;
        SaveJsonFile(SessionFilePath, _sessionCache);
    }

    private static void SaveJsonFile(string path, Dictionary<string, JsonElement> cache)
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder);

            // Build a sorted dictionary for stable output.
            // Use JsonNode so nested objects (e.g. theme overrides) are written
            // as proper JSON objects rather than escaped strings.
            var sorted = new SortedDictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.Ordinal);
            foreach (var (key, element) in cache)
            {
                sorted[key] = System.Text.Json.Nodes.JsonNode.Parse(element.GetRawText());
            }

            string json = JsonSerializer.Serialize(sorted, WriteOptions);

            // Atomic write: write to .tmp then rename.
            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);
        }
        catch { }
    }

    // ── One-time migration from registry ────────────────────────────

    private const string RegistryKeyPath = @"Software\Bascanka";
    private const string SessionRegistryKeyPath = @"Software\Bascanka\Session";

    private static void MigrateFromRegistry()
    {
        try
        {
            using var mainKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (mainKey is null) // Fresh install, no registry data.
            {
                _settingsCache = new Dictionary<string, JsonElement>();
                _sessionCache = new Dictionary<string, JsonElement>();
                return;
            }

            // Migrate main settings.
            _settingsCache = new Dictionary<string, JsonElement>();
            foreach (string name in mainKey.GetValueNames())
            {
                var val = mainKey.GetValue(name);
                if (val is string s)
                    _settingsCache[name] = ToJsonElement(s);
                else if (val is int i)
                    _settingsCache[name] = ToJsonElement(i);
            }

            // Migrate session sub-key.
            _sessionCache = new Dictionary<string, JsonElement>();
            using var sessionKey = Registry.CurrentUser.OpenSubKey(SessionRegistryKeyPath);
            if (sessionKey is not null)
            {
                foreach (string name in sessionKey.GetValueNames())
                {
                    var val = sessionKey.GetValue(name);
                    if (val is string s)
                        _sessionCache[name] = ToJsonElement(s);
                    else if (val is int i)
                        _sessionCache[name] = ToJsonElement(i);
                }
            }

            // Save both JSON files.
            SaveSettings();
            SaveSession();

            // Clean up registry.
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // If migration fails, start with empty caches.
            _settingsCache ??= new Dictionary<string, JsonElement>();
            _sessionCache ??= new Dictionary<string, JsonElement>();
        }
    }

    private static JsonElement ToJsonElement(string value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    private static JsonElement ToJsonElement(int value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    private static JsonElement ToJsonElement(bool value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    /// <summary>Parses a raw JSON fragment (e.g. an object or array) into a JsonElement.</summary>
    private static JsonElement ParseRawJsonElement(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        return doc.RootElement.Clone();
    }

    // ── Public API: Settings ────────────────────────────────────────

    /// <summary>Gets a string value, or the default if not found.</summary>
    public static string GetString(string name, string defaultValue = "")
    {
        try
        {
            EnsureLoaded();
            if (_settingsCache!.TryGetValue(name, out var el))
            {
                return el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString() ?? defaultValue,
                    JsonValueKind.Number => el.GetRawText(),
                    JsonValueKind.True => "1",
                    JsonValueKind.False => "0",
                    _ => defaultValue,
                };
            }
        }
        catch { }
        return defaultValue;
    }

    /// <summary>Sets a string value.</summary>
    public static void SetString(string name, string value)
    {
        try
        {
            EnsureLoaded();
            _settingsCache![name] = ToJsonElement(value);
            SaveSettings();
        }
        catch { }
    }

    /// <summary>Gets a boolean value.</summary>
    public static bool GetBool(string name, bool defaultValue = false)
    {
        try
        {
            EnsureLoaded();
            if (_settingsCache!.TryGetValue(name, out var el))
            {
                return el.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number when el.TryGetInt32(out int i) => i != 0,
                    JsonValueKind.String when bool.TryParse(el.GetString(), out bool b) => b,
                    _ => defaultValue,
                };
            }
        }
        catch { }
        return defaultValue;
    }

    /// <summary>Sets a boolean value.</summary>
    public static void SetBool(string name, bool value)
    {
        try
        {
            EnsureLoaded();
            _settingsCache![name] = ToJsonElement(value);
            SaveSettings();
        }
        catch { }
    }

    /// <summary>Gets an integer value.</summary>
    public static int GetInt(string name, int defaultValue = 0)
    {
        try
        {
            EnsureLoaded();
            if (_settingsCache!.TryGetValue(name, out var el))
            {
                return el.ValueKind switch
                {
                    JsonValueKind.Number when el.TryGetInt32(out int i) => i,
                    JsonValueKind.String when int.TryParse(el.GetString(), out int i) => i,
                    _ => defaultValue,
                };
            }
        }
        catch { }
        return defaultValue;
    }

    /// <summary>Sets an integer value.</summary>
    public static void SetInt(string name, int value)
    {
        try
        {
            EnsureLoaded();
            _settingsCache![name] = ToJsonElement(value);
            SaveSettings();
        }
        catch { }
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
    public const string KeyMenuItemPadding = "MenuItemPadding";
    public const string KeyRecentFilesSeparated = "RecentFilesSeparated";
    public const string KeyTerminalPadding = "TerminalPadding";

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
    public const string KeyAutoSaveInterval = "AutoSaveInterval";

    /// <summary>
    /// Deletes all settings, preserving session state.
    /// </summary>
    public static void ResetToDefaults()
    {
        try
        {
            _settingsCache = new Dictionary<string, JsonElement>();
            if (File.Exists(SettingsFilePath))
                File.Delete(SettingsFilePath);
        }
        catch { }
    }

    // ── Session ─────────────────────────────────────────────────────

    /// <summary>Gets an integer from session state.</summary>
    public static int GetSessionInt(string name, int defaultValue = 0)
    {
        try
        {
            EnsureSessionLoaded();
            if (_sessionCache!.TryGetValue(name, out var el))
            {
                return el.ValueKind switch
                {
                    JsonValueKind.Number when el.TryGetInt32(out int i) => i,
                    JsonValueKind.String when int.TryParse(el.GetString(), out int i) => i,
                    _ => defaultValue,
                };
            }
        }
        catch { }
        return defaultValue;
    }

    /// <summary>Sets an integer in session state.</summary>
    public static void SetSessionInt(string name, int value)
    {
        try
        {
            EnsureSessionLoaded();
            _sessionCache![name] = ToJsonElement(value);
            SaveSession();
        }
        catch { }
    }

    /// <summary>Gets a string from session state.</summary>
    public static string GetSessionString(string name, string defaultValue = "")
    {
        try
        {
            EnsureSessionLoaded();
            if (_sessionCache!.TryGetValue(name, out var el))
            {
                return el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString() ?? defaultValue,
                    JsonValueKind.Number => el.GetRawText(),
                    _ => defaultValue,
                };
            }
        }
        catch { }
        return defaultValue;
    }

    /// <summary>Sets a string in session state.</summary>
    public static void SetSessionString(string name, string value)
    {
        try
        {
            EnsureSessionLoaded();
            _sessionCache![name] = ToJsonElement(value);
            SaveSession();
        }
        catch { }
    }

    /// <summary>Deletes all session state.</summary>
    public static void ClearSessionState()
    {
        try
        {
            _sessionCache = new Dictionary<string, JsonElement>();
            if (File.Exists(SessionFilePath))
                File.Delete(SessionFilePath);
        }
        catch { }
    }

    /// <summary>Saves structured session data as JSON, replacing the session file.</summary>
    public static void SaveStructuredSession(object data)
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder);
            string json = JsonSerializer.Serialize(data, data.GetType(), WriteOptions);

            // Atomic write: write to .tmp then rename.
            string tmpPath = SessionFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, SessionFilePath, overwrite: true);
            _sessionCache = null; // invalidate legacy flat cache
        }
        catch { }
    }

    /// <summary>Reads the session file and returns the root JSON element, or null if not found.</summary>
    public static JsonElement? ReadSessionRoot()
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder);
            if (File.Exists(SessionFilePath))
            {
                string json = File.ReadAllText(SessionFilePath);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
        }
        catch { }
        return null;
    }

    // ── Explorer context menu (remains in registry) ─────────────────

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
            shellKey.SetValue("", Strings.ContextMenuEditWith);
            shellKey.SetValue("Icon", $"\"{exePath}\",0");

            using var cmdKey = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\" + ExplorerCommandKeyPath);
            cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch { }
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
        catch { }
    }

    // ── New Explorer context menu (sparse package + COM DLL) ────────

    private const string NewExplorerClsid = "4E5E2661-E088-4309-98FF-E512A6BCD639";
    private const string NewExplorerRegistryKey = @"Software\Bascanka\ContextMenu";

    private static readonly string NewExplorerExtractDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Bascanka", "ContextMenu");

    /// <summary>
    /// Returns whether the new Explorer context menu registration appears to be in place.
    /// </summary>
    public static bool IsNewExplorerContextMenuRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(NewExplorerRegistryKey);
            if (key is null) return false;
            string? dllPath = key.GetValue("DllPath") as string;
            return dllPath is not null && File.Exists(dllPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers the new Explorer context menu via a sparse AppX package.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public static string? RegisterNewExplorerContextMenu()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;

            // 1. Create extraction directories.
            string assetsDir = Path.Combine(NewExplorerExtractDir, "Assets");
            Directory.CreateDirectory(assetsDir);

            // 2. Extract embedded resources.
            string dllPath = Path.Combine(NewExplorerExtractDir, "Bascanka.Explorer.ContextMenu.dll");
            ExtractEmbeddedResource("Bascanka.App.Resources.Bascanka.Explorer.ContextMenu.dll", dllPath);

            string iconPath = Path.Combine(NewExplorerExtractDir, "bascanka.ico");
            ExtractEmbeddedResource("Bascanka.App.Resources.bascanka.ico", iconPath);

            string logoPath = Path.Combine(assetsDir, "logo.png");
            ExtractEmbeddedResource("Bascanka.App.Resources.bascanka_logo.png", logoPath);

            // 3. Generate manifest from template.
            string manifestPath = Path.Combine(NewExplorerExtractDir, "AppxManifest.xml");
            string? template = ReadEmbeddedResourceText("Bascanka.App.Resources.AppxManifest.xml.template");
            if (template is null)
                return "Manifest template not found in embedded resources.";

            string exeName = Path.GetFileName(exePath);
            string displayName = Strings.ContextMenuEditWith;
            string manifest = template
                .Replace("$$DISPLAY_NAME$$", displayName)
                .Replace("$$CLSID$$", NewExplorerClsid)
                .Replace("$$EXECUTABLE$$", exeName);
            File.WriteAllText(manifestPath, manifest);

            // 4. Write registry configuration (the COM DLL reads these values).
            //    CLSID must be braced for CLSIDFromString in the C++ DLL.
            using var regKey = Registry.CurrentUser.CreateSubKey(NewExplorerRegistryKey);
            regKey.SetValue("CLSID", "{" + NewExplorerClsid + "}");
            regKey.SetValue("DisplayName", displayName);
            regKey.SetValue("ExePath", exePath);
            regKey.SetValue("IconPath", iconPath);
            regKey.SetValue("DllPath", dllPath);

            // 5. Register COM DLL.
            var regsvr = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "regsvr32.exe",
                Arguments = $"/s \"{dllPath}\"",
                UseShellExecute = true
            };
            using var regProc = System.Diagnostics.Process.Start(regsvr);
            regProc?.WaitForExit(15_000);

            // 6. Register sparse AppX package via PowerShell.
            string escapedDir = NewExplorerExtractDir.Replace("'", "''");
            string escapedManifest = manifestPath.Replace("'", "''");
            string psScript = $"Add-AppxPackage -ExternalLocation '{escapedDir}' -Register '{escapedManifest}'";
            var psInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            using var psProc = System.Diagnostics.Process.Start(psInfo);
            string psError = psProc?.StandardError.ReadToEnd() ?? "";
            psProc?.WaitForExit(30_000);

            if (psProc is not null && psProc.ExitCode != 0 && !string.IsNullOrWhiteSpace(psError))
                return psError.Trim();

            return null; // success
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Unregisters the new Explorer context menu.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public static string? UnregisterNewExplorerContextMenu()
    {
        try
        {
            // 1. Remove sparse AppX package.
            string psScript = "$pkg = Get-AppxPackage -Name 'Bascanka.ShellExtension' -ErrorAction SilentlyContinue; if ($pkg) { Remove-AppxPackage -Package $pkg.PackageFullName }";
            var psInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var psProc = System.Diagnostics.Process.Start(psInfo);
            psProc?.WaitForExit(30_000);

            // 2. Unregister COM DLL.
            string? dllPath = null;
            try
            {
                using var regKey = Registry.CurrentUser.OpenSubKey(NewExplorerRegistryKey);
                dllPath = regKey?.GetValue("DllPath") as string;
            }
            catch { }

            if (dllPath is not null && File.Exists(dllPath))
            {
                var regsvr = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "regsvr32.exe",
                    Arguments = $"/u /s \"{dllPath}\"",
                    UseShellExecute = true
                };
                using var regProc = System.Diagnostics.Process.Start(regsvr);
                regProc?.WaitForExit(15_000);
            }

            // 3. Delete only the ContextMenu registry key.
            Registry.CurrentUser.DeleteSubKeyTree(NewExplorerRegistryKey, throwOnMissingSubKey: false);

            return null; // success
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Kills all Explorer processes, waits, then relaunches Explorer.
    /// </summary>
    public static void RestartExplorer()
    {
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                try { proc.Kill(); } catch { }
                proc.Dispose();
            }

            Thread.Sleep(1500);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private static void ExtractEmbeddedResource(string resourceName, string targetPath)
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return;

        using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
        stream.CopyTo(fs);
    }

    private static string? ReadEmbeddedResourceText(string resourceName)
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ── Theme overrides ─────────────────────────────────────────────

    /// <summary>
    /// Gets the JSON string of colour overrides for the specified theme, or null if none.
    /// </summary>
    public static string? GetThemeOverrides(string themeName)
    {
        try
        {
            EnsureLoaded();
            string key = KeyThemeOverridesPrefix + themeName;
            if (_settingsCache!.TryGetValue(key, out var el))
            {
                // New format: stored as a JSON object.
                if (el.ValueKind == JsonValueKind.Object)
                    return el.GetRawText();
                // Legacy format: stored as an escaped JSON string.
                if (el.ValueKind == JsonValueKind.String)
                {
                    string? s = el.GetString();
                    return string.IsNullOrEmpty(s) ? null : s;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Stores (or clears) the JSON colour overrides for the specified theme.
    /// </summary>
    public static void SetThemeOverrides(string themeName, string? json)
    {
        string key = KeyThemeOverridesPrefix + themeName;
        if (string.IsNullOrWhiteSpace(json))
        {
            try
            {
                EnsureLoaded();
                _settingsCache!.Remove(key);
                SaveSettings();
            }
            catch { }
        }
        else
        {
            try
            {
                EnsureLoaded();
                _settingsCache![key] = ParseRawJsonElement(json);
                SaveSettings();
            }
            catch { }
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
        KeyTabHeight, KeyMinTabWidth, KeyMenuItemPadding, KeyRecentFilesSeparated, KeyTerminalPadding,
        KeyLargeFileThresholdMB, KeyFoldingMaxFileSizeMB,
        KeyMaxRecentFiles, KeySearchHistoryLimit, KeySearchDebounce,
    ];

    /// <summary>
    /// Serializes all settings and theme overrides to a JSON string.
    /// </summary>
    public static string ExportToJson()
    {
        EnsureLoaded();

        var settings = new Dictionary<string, object>();
        foreach (string name in ExportableKeys)
        {
            if (_settingsCache!.TryGetValue(name, out var el))
            {
                object? val = el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString(),
                    JsonValueKind.Number when el.TryGetInt32(out int i) => i,
                    JsonValueKind.True => 1,
                    JsonValueKind.False => 0,
                    _ => null,
                };
                if (val is not null)
                    settings[name] = val;
            }
        }

        var themeOverrides = new Dictionary<string, System.Text.Json.Nodes.JsonNode?>();
        foreach (var (name, el) in _settingsCache!)
        {
            if (name.StartsWith(KeyThemeOverridesPrefix, StringComparison.Ordinal))
            {
                string themeName = name[KeyThemeOverridesPrefix.Length..];
                if (el.ValueKind == JsonValueKind.Object)
                    themeOverrides[themeName] = System.Text.Json.Nodes.JsonNode.Parse(el.GetRawText());
                else if (el.ValueKind == JsonValueKind.String)
                    themeOverrides[themeName] = System.Text.Json.Nodes.JsonNode.Parse(el.GetString()!);
            }
        }

        var root = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["settings"] = settings,
            ["themeOverrides"] = themeOverrides,
        };

        return JsonSerializer.Serialize(root, WriteOptions);
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
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    SetThemeOverrides(prop.Name, prop.Value.GetRawText());
                else if (prop.Value.ValueKind == JsonValueKind.String)
                    SetThemeOverrides(prop.Name, prop.Value.GetString());
            }
        }
    }
}
