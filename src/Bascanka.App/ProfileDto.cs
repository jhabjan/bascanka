using System.Text.Json.Serialization;

namespace Bascanka.App;


internal sealed class ProfileDto
{
	[JsonPropertyName("name")]
	public string? Name { get; set; }

	[JsonPropertyName("rules")]
	public List<RuleDto>? Rules { get; set; }
}

