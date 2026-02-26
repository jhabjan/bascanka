using System.Text.Json.Serialization;

namespace Bascanka.App;

internal sealed class RuleDto
{
	[JsonPropertyName("pattern")]
	public string? Pattern { get; set; }

	[JsonPropertyName("scope")]
	public string? Scope { get; set; }

	[JsonPropertyName("foreground")]
	public string? Foreground { get; set; }

	[JsonPropertyName("background")]
	public string? Background { get; set; }

	[JsonPropertyName("begin")]
	public string? Begin { get; set; }

	[JsonPropertyName("end")]
	public string? End { get; set; }

	[JsonPropertyName("foldable")]
	public bool? Foldable { get; set; }
}

