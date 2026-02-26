using System.Text.Json.Serialization;

namespace Bascanka.App;

internal sealed class RootDto
{
	[JsonPropertyName("custom_highlighting")]
	public List<ProfileDto>? CustomHighlighting { get; set; }
}

