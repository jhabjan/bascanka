using System.Reflection;
using System.Text.Json;

namespace Bascanka.App;

/// <summary>
/// Manages UI localization. English and Croatian are embedded; additional
/// languages can be added by dropping JSON files into the <c>languages/</c>
/// folder next to the executable.
/// </summary>
internal static class LocalizationManager
{
    private static readonly string[] EmbeddedLanguages = ["en", "hr", "sr","zh"];

    private static Dictionary<string, string> _active = new();
    private static Dictionary<string, string> _fallback = new(); // always English

    /// <summary>Currently loaded language code (e.g. "en", "hr").</summary>
    public static string CurrentLanguage { get; private set; } = "en";

    /// <summary>Fired after <see cref="LoadLanguage"/> completes.</summary>
    public static event Action? LanguageChanged;

    /// <summary>
    /// Initializes the manager: loads English as fallback, then loads the
    /// saved preference (or English if none saved).
    /// </summary>
    public static void Initialize()
    {
        _fallback = LoadStrings("en");
        _active = _fallback;
        CurrentLanguage = "en";

        string saved = ReadSavedLanguage();
        if (!string.IsNullOrEmpty(saved) && saved != "en")
        {
            LoadLanguage(saved);
        }
    }

    /// <summary>
    /// Switches the UI language. Loads the string dictionary for
    /// <paramref name="langCode"/> and fires <see cref="LanguageChanged"/>.
    /// </summary>
    public static void LoadLanguage(string langCode)
    {
        var strings = LoadStrings(langCode);
        if (strings.Count == 0)
        {
            // Language file not found — keep current.
            return;
        }

        _active = strings;
        CurrentLanguage = langCode;
        SaveLanguagePreference(langCode);
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// Returns a localized string by key. Falls back to English, then to
    /// the key itself if not found in either dictionary.
    /// </summary>
    public static string Get(string key)
    {
        if (_active.TryGetValue(key, out var value))
            return value;
        if (_fallback.TryGetValue(key, out var fallback))
            return fallback;
        return key;
    }

    /// <summary>
    /// Returns all available languages (embedded + external).
    /// </summary>
    public static List<(string Code, string DisplayName)> GetAvailableLanguages()
    {
        var result = new List<(string Code, string DisplayName)>();

        // Embedded languages.
        foreach (string code in EmbeddedLanguages)
        {
            string name = GetLanguageName(code, embedded: true);
            if (name.Length > 0)
                result.Add((code, name));
        }

        // External languages from {appDir}/languages/*.json
        string langDir = GetExternalLanguagesDir();
        if (Directory.Exists(langDir))
        {
            foreach (string file in Directory.GetFiles(langDir, "lang.*.json"))
            {
                string fileName = Path.GetFileNameWithoutExtension(file); // "lang.de"
                string[] parts = fileName.Split('.');
                if (parts.Length >= 2)
                {
                    string code = parts[1];
                    // Skip if already covered by embedded.
                    if (result.Exists(r => r.Code == code))
                        continue;

                    string name = GetLanguageNameFromFile(file);
                    if (name.Length > 0)
                        result.Add((code, name));
                }
            }
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ────────────────────────────────────────────────────────────────────

    private static Dictionary<string, string> LoadStrings(string langCode)
    {
        // Try embedded resource first.
        string? json = LoadEmbeddedJson(langCode);

        // Then try external file.
        if (json is null)
        {
            string path = Path.Combine(GetExternalLanguagesDir(), $"lang.{langCode}.json");
            if (File.Exists(path))
                json = File.ReadAllText(path);
        }

        if (json is null)
            return new Dictionary<string, string>();

        return ParseLanguageJson(json);
    }

    private static string? LoadEmbeddedJson(string langCode)
    {
        var asm = Assembly.GetExecutingAssembly();
        string resourceName = $"Bascanka.App.Resources.lang_{langCode}.json";
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, string> ParseLanguageJson(string json)
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("strings", out var strings))
            {
                foreach (var prop in strings.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
            // Malformed JSON — return empty.
        }
        return result;
    }

    private static string GetLanguageName(string langCode, bool embedded)
    {
        string? json = embedded ? LoadEmbeddedJson(langCode) : null;
        if (json is null) return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("languageName", out var name))
                return name.GetString() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    private static string GetLanguageNameFromFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("languageName", out var name))
                return name.GetString() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    private static string GetExternalLanguagesDir()
    {
        string appDir = AppContext.BaseDirectory;
        return Path.Combine(appDir, "languages");
    }

    private static string GetSettingsDir()
    {
        string appDir = AppContext.BaseDirectory;
        return Path.Combine(appDir, "settings");
    }

    private static string ReadSavedLanguage()
    {
        // Try registry first, fall back to legacy file.
        string fromRegistry = SettingsManager.GetString(SettingsManager.KeyLanguage);
        if (!string.IsNullOrEmpty(fromRegistry))
            return fromRegistry;

        try
        {
            string file = Path.Combine(GetSettingsDir(), "language.txt");
            if (File.Exists(file))
            {
                string lang = File.ReadAllText(file).Trim();
                // Migrate to registry and clean up old file.
                if (!string.IsNullOrEmpty(lang))
                {
                    SettingsManager.SetString(SettingsManager.KeyLanguage, lang);
                    try { File.Delete(file); } catch { }
                }
                return lang;
            }
        }
        catch { }
        return string.Empty;
    }

    private static void SaveLanguagePreference(string langCode)
    {
        SettingsManager.SetString(SettingsManager.KeyLanguage, langCode);
    }
}
