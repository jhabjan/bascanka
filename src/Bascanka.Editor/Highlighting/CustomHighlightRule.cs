namespace Bascanka.Editor.Highlighting;

/// <summary>
/// A single highlighting rule — either line-level or match-level.
/// </summary>
public sealed class CustomHighlightRule
{
	/// <summary>Regex pattern to match against line text (used by line/match scopes).</summary>
	public string Pattern { get; set; } = string.Empty;

	/// <summary>"line", "match", or "block".</summary>
	public string Scope { get; set; } = "match";

	/// <summary>Foreground color. <see cref="Color.Empty"/> = use default.</summary>
	public Color Foreground { get; set; } = Color.Empty;

	/// <summary>Background color. <see cref="Color.Empty"/> = transparent.</summary>
	public Color Background { get; set; } = Color.Empty;

	/// <summary>Begin pattern for block-scope rules.</summary>
	public string BeginPattern { get; set; } = string.Empty;

	/// <summary>End pattern for block-scope rules.</summary>
	public string EndPattern { get; set; } = string.Empty;

	/// <summary>Whether block regions are foldable in the gutter.</summary>
	public bool Foldable { get; set; }
}
