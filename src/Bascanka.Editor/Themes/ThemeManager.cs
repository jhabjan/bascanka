using System.Drawing;
using System.Text.Json;
using Bascanka.Core.Syntax;

namespace Bascanka.Editor.Themes;

/// <summary>
/// Singleton manager that owns the active <see cref="ITheme"/> and provides
/// registration, selection, and JSON-based custom theme loading.
/// Controls subscribe to <see cref="ThemeChanged"/> to repaint when the theme switches.
/// </summary>
public sealed class ThemeManager
{
    // ── Singleton ─────────────────────────────────────────────────────

    private static readonly Lazy<ThemeManager> _lazy = new(() => new ThemeManager());

    /// <summary>
    /// The process-wide <see cref="ThemeManager"/> instance.
    /// </summary>
    public static ThemeManager Instance => _lazy.Value;

    // ── State ─────────────────────────────────────────────────────────

    private readonly Dictionary<string, ITheme> _themes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ITheme> _baseThemes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Color>> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private ITheme _currentTheme;
    private ITheme _fallbackTheme;

    private ThemeManager()
    {
        // Register the two built-in themes and default to System.
        var dark = new DarkTheme();
        var light = new LightTheme();
        var system = new SystemTheme();
        _fallbackTheme = dark;
        _themes[dark.Name] = dark;
        _themes[light.Name] = light;
        _themes[system.Name] = system;
        _baseThemes[dark.Name] = dark;
        _baseThemes[light.Name] = light;
        _baseThemes[system.Name] = system;
        _currentTheme = system;
    }

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// The theme currently applied across the editor.
    /// Setting this property directly also raises <see cref="ThemeChanged"/>.
    /// </summary>
    public ITheme CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (ReferenceEquals(_currentTheme, value))
                return;
            _currentTheme = value;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Raised after the active theme has been switched.
    /// All editor controls should subscribe to this event and invalidate themselves.
    /// </summary>
    public event EventHandler? ThemeChanged;

    /// <summary>
    /// Returns the names of all registered themes in registration order.
    /// </summary>
    public IReadOnlyList<string> ThemeNames => _themes.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Registers a theme. If a theme with the same <see cref="ITheme.Name"/>
    /// already exists it is replaced.
    /// </summary>
    public void RegisterTheme(ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _themes[theme.Name] = theme;
        if (!_baseThemes.ContainsKey(theme.Name))
            _baseThemes[theme.Name] = theme;
    }

    /// <summary>
    /// Activates the theme identified by <paramref name="name"/>.
    /// The comparison is case-insensitive.
    /// </summary>
    public void SetTheme(string name)
    {
        if (_themes.TryGetValue(name, out var theme))
        {
            CurrentTheme = theme;
        }
        else
        {
            theme = _fallbackTheme;
        }
    }

    /// <summary>
    /// Returns the base (built-in) theme for the given name, ignoring overrides.
    /// </summary>
    public ITheme? GetBaseTheme(string themeName)
    {
        _baseThemes.TryGetValue(themeName, out var theme);
        return theme;
    }

    /// <summary>
    /// Returns the current colour override dictionary for the given theme, or null if none.
    /// </summary>
    public Dictionary<string, Color>? GetOverrides(string themeName)
    {
        _overrides.TryGetValue(themeName, out var dict);
        return dict;
    }

    /// <summary>
    /// Applies (or clears) colour overrides on top of the base theme.
    /// If <paramref name="overrides"/> is null or empty the theme reverts to its base.
    /// Re-fires <see cref="ThemeChanged"/> if the affected theme is the current one.
    /// </summary>
    public void ApplyOverrides(string themeName, Dictionary<string, Color>? overrides)
    {
        if (!_baseThemes.TryGetValue(themeName, out var baseTheme))
            return;

        if (overrides is null || overrides.Count == 0)
        {
            _overrides.Remove(themeName);
            _themes[themeName] = baseTheme;
        }
        else
        {
            _overrides[themeName] = overrides;
            _themes[themeName] = new JsonTheme(themeName, overrides, baseTheme);
        }

        // Re-fire if it affects the active theme.
        if (string.Equals(_currentTheme.Name, themeName, StringComparison.OrdinalIgnoreCase))
        {
            _currentTheme = _themes[themeName];
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Reads a colour from a theme by property name using reflection.
    /// Returns null if the property does not exist.
    /// </summary>
    public static Color? GetThemeColor(ITheme theme, string propertyName)
    {
        var prop = typeof(ITheme).GetProperty(propertyName);
        if (prop is not null && prop.PropertyType == typeof(Color))
            return (Color)prop.GetValue(theme)!;
        return null;
    }

    /// <summary>
    /// Loads a custom theme from a JSON file, registers it, and returns its name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The JSON file must contain an object with a <c>"name"</c> property and
    /// colour entries keyed by the <see cref="ITheme"/> property names as well
    /// as <see cref="TokenType"/> member names prefixed with <c>"Token."</c>.
    /// </para>
    /// <para>Example:</para>
    /// <code>
    /// {
    ///   "name": "My Theme",
    ///   "EditorBackground": "#1E1E1E",
    ///   "EditorForeground": "#D4D4D4",
    ///   "Token.Keyword": "#569CD6",
    ///   "Token.String": "#CE9178"
    /// }
    /// </code>
    /// Any colour entry not specified in the JSON falls back to the
    /// built-in Dark theme value.
    /// </remarks>
    public string LoadThemeFromJson(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var root = doc.RootElement;

        if (!root.TryGetProperty("name", out var nameProp) ||
            nameProp.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Theme JSON must contain a \"name\" property with a string value.");
        }

        string themeName = nameProp.GetString()!;
        var fallback = new DarkTheme();
        var colours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                string colourText = property.Value.GetString()!;
                try
                {
                    colours[property.Name] = ColorTranslator.FromHtml(colourText);
                }
                catch (Exception)
                {
                    // Skip colours that cannot be parsed.
                }
            }
        }

        var theme = new JsonTheme(themeName, colours, fallback);
        RegisterTheme(theme);
        return themeName;
    }

    // ── JSON-based theme implementation ───────────────────────────────

    /// <summary>
    /// An <see cref="ITheme"/> implementation that is driven by a dictionary
    /// of colour values loaded from a JSON file, with a fallback theme for
    /// any unspecified colours.
    /// </summary>
    private sealed class JsonTheme : ITheme
    {
        private readonly Dictionary<string, Color> _colours;
        private readonly ITheme _fallback;

        public JsonTheme(string name, Dictionary<string, Color> colours, ITheme fallback)
        {
            Name = name;
            _colours = colours;
            _fallback = fallback;
        }

        public string Name { get; }

        public Color GetTokenColor(TokenType type)
        {
            // Try "Token.Keyword" style keys.
            string key = $"Token.{type}";
            if (_colours.TryGetValue(key, out var colour))
                return colour;
            return _fallback.GetTokenColor(type);
        }

        // Helper that looks up a colour by the property name, falling back
        // to the corresponding property on the fallback theme.
        private Color Get(string key, Func<ITheme, Color> fallbackSelector)
        {
            if (_colours.TryGetValue(key, out var colour))
                return colour;
            return fallbackSelector(_fallback);
        }

        public Color EditorBackground      => Get(nameof(EditorBackground),      t => t.EditorBackground);
        public Color EditorForeground      => Get(nameof(EditorForeground),      t => t.EditorForeground);
        public Color GutterBackground      => Get(nameof(GutterBackground),      t => t.GutterBackground);
        public Color GutterForeground      => Get(nameof(GutterForeground),      t => t.GutterForeground);
        public Color GutterCurrentLine     => Get(nameof(GutterCurrentLine),     t => t.GutterCurrentLine);
        public Color LineHighlight         => Get(nameof(LineHighlight),         t => t.LineHighlight);
        public Color SelectionBackground   => Get(nameof(SelectionBackground),   t => t.SelectionBackground);
        public Color SelectionForeground   => Get(nameof(SelectionForeground),   t => t.SelectionForeground);
        public Color CaretColor            => Get(nameof(CaretColor),            t => t.CaretColor);
        public Color TabBarBackground      => Get(nameof(TabBarBackground),      t => t.TabBarBackground);
        public Color TabActiveBackground   => Get(nameof(TabActiveBackground),   t => t.TabActiveBackground);
        public Color TabInactiveBackground => Get(nameof(TabInactiveBackground), t => t.TabInactiveBackground);
        public Color TabActiveForeground   => Get(nameof(TabActiveForeground),   t => t.TabActiveForeground);
        public Color TabInactiveForeground => Get(nameof(TabInactiveForeground), t => t.TabInactiveForeground);
        public Color TabBorder             => Get(nameof(TabBorder),             t => t.TabBorder);
        public Color StatusBarBackground   => Get(nameof(StatusBarBackground),   t => t.StatusBarBackground);
        public Color StatusBarForeground   => Get(nameof(StatusBarForeground),   t => t.StatusBarForeground);
        public Color FindPanelBackground   => Get(nameof(FindPanelBackground),   t => t.FindPanelBackground);
        public Color FindPanelForeground   => Get(nameof(FindPanelForeground),   t => t.FindPanelForeground);
        public Color MatchHighlight        => Get(nameof(MatchHighlight),        t => t.MatchHighlight);
        public Color BracketMatchBackground => Get(nameof(BracketMatchBackground), t => t.BracketMatchBackground);
        public Color MenuBackground        => Get(nameof(MenuBackground),        t => t.MenuBackground);
        public Color MenuForeground        => Get(nameof(MenuForeground),        t => t.MenuForeground);
        public Color MenuHighlight         => Get(nameof(MenuHighlight),         t => t.MenuHighlight);
        public Color ScrollBarBackground   => Get(nameof(ScrollBarBackground),   t => t.ScrollBarBackground);
        public Color ScrollBarThumb        => Get(nameof(ScrollBarThumb),        t => t.ScrollBarThumb);
        public Color DiffAddedBackground        => Get(nameof(DiffAddedBackground),        t => t.DiffAddedBackground);
        public Color DiffRemovedBackground      => Get(nameof(DiffRemovedBackground),      t => t.DiffRemovedBackground);
        public Color DiffModifiedBackground     => Get(nameof(DiffModifiedBackground),     t => t.DiffModifiedBackground);
        public Color DiffModifiedCharBackground => Get(nameof(DiffModifiedCharBackground), t => t.DiffModifiedCharBackground);
        public Color DiffPaddingBackground      => Get(nameof(DiffPaddingBackground),      t => t.DiffPaddingBackground);
        public Color DiffGutterMarker           => Get(nameof(DiffGutterMarker),           t => t.DiffGutterMarker);
        public Color FoldingMarker         => Get(nameof(FoldingMarker),         t => t.FoldingMarker);
        public Color ModifiedIndicator     => Get(nameof(ModifiedIndicator),     t => t.ModifiedIndicator);
    }
}
