using Bascanka.Core.Syntax;
namespace Bascanka.Editor.Themes;
// ── JSON-based theme implementation ───────────────────────────────

/// <summary>
/// An <see cref="ITheme"/> implementation that is driven by a dictionary
/// of colour values loaded from a JSON file, with a fallback theme for
/// any unspecified colours.
/// </summary>
internal sealed class JsonTheme(string name, Dictionary<string, Color> colours, ITheme fallback) : ITheme
{
	private readonly Dictionary<string, Color> _colours = colours;
	private readonly ITheme _fallback = fallback;

	public string Name { get; } = name;

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

	public Color EditorBackground => Get(nameof(EditorBackground), t => t.EditorBackground);
	public Color EditorForeground => Get(nameof(EditorForeground), t => t.EditorForeground);
	public Color GutterBackground => Get(nameof(GutterBackground), t => t.GutterBackground);
	public Color GutterForeground => Get(nameof(GutterForeground), t => t.GutterForeground);
	public Color GutterCurrentLine => Get(nameof(GutterCurrentLine), t => t.GutterCurrentLine);
	public Color LineHighlight => Get(nameof(LineHighlight), t => t.LineHighlight);
	public Color SelectionBackground => Get(nameof(SelectionBackground), t => t.SelectionBackground);
	public Color SelectionForeground => Get(nameof(SelectionForeground), t => t.SelectionForeground);
	public Color CaretColor => Get(nameof(CaretColor), t => t.CaretColor);
	public Color TabBarBackground => Get(nameof(TabBarBackground), t => t.TabBarBackground);
	public Color TabActiveBackground => Get(nameof(TabActiveBackground), t => t.TabActiveBackground);
	public Color TabInactiveBackground => Get(nameof(TabInactiveBackground), t => t.TabInactiveBackground);
	public Color TabActiveForeground => Get(nameof(TabActiveForeground), t => t.TabActiveForeground);
	public Color TabInactiveForeground => Get(nameof(TabInactiveForeground), t => t.TabInactiveForeground);
	public Color TabBorder => Get(nameof(TabBorder), t => t.TabBorder);
	public Color StatusBarBackground => Get(nameof(StatusBarBackground), t => t.StatusBarBackground);
	public Color StatusBarForeground => Get(nameof(StatusBarForeground), t => t.StatusBarForeground);
	public Color FindPanelBackground => Get(nameof(FindPanelBackground), t => t.FindPanelBackground);
	public Color FindPanelForeground => Get(nameof(FindPanelForeground), t => t.FindPanelForeground);
	public Color MatchHighlight => Get(nameof(MatchHighlight), t => t.MatchHighlight);
	public Color BracketMatchBackground => Get(nameof(BracketMatchBackground), t => t.BracketMatchBackground);
	public Color MenuBackground => Get(nameof(MenuBackground), t => t.MenuBackground);
	public Color MenuForeground => Get(nameof(MenuForeground), t => t.MenuForeground);
	public Color MenuHighlight => Get(nameof(MenuHighlight), t => t.MenuHighlight);
	public Color ScrollBarBackground => Get(nameof(ScrollBarBackground), t => t.ScrollBarBackground);
	public Color ScrollBarThumb => Get(nameof(ScrollBarThumb), t => t.ScrollBarThumb);
	public Color DiffAddedBackground => Get(nameof(DiffAddedBackground), t => t.DiffAddedBackground);
	public Color DiffRemovedBackground => Get(nameof(DiffRemovedBackground), t => t.DiffRemovedBackground);
	public Color DiffModifiedBackground => Get(nameof(DiffModifiedBackground), t => t.DiffModifiedBackground);
	public Color DiffModifiedCharBackground => Get(nameof(DiffModifiedCharBackground), t => t.DiffModifiedCharBackground);
	public Color DiffPaddingBackground => Get(nameof(DiffPaddingBackground), t => t.DiffPaddingBackground);
	public Color DiffGutterMarker => Get(nameof(DiffGutterMarker), t => t.DiffGutterMarker);
	public Color FoldingMarker => Get(nameof(FoldingMarker), t => t.FoldingMarker);
	public Color ModifiedIndicator => Get(nameof(ModifiedIndicator), t => t.ModifiedIndicator);
}

