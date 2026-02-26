using System.Drawing;

namespace Bascanka.Editor.Highlighting;

/// <summary>
/// A named collection of custom highlighting rules.
/// </summary>
public sealed class CustomHighlightProfile
{
    public string Name { get; set; } = string.Empty;
    public List<CustomHighlightRule> Rules { get; set; } = [];
}
