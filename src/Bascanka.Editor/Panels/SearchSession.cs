using Bascanka.Core.Search;

namespace Bascanka.Editor.Panels;

internal sealed class SearchSession
{
	public required string Pattern { get; init; }
	public required string ScopeLabel { get; init; }
	public required List<SearchResult> Results { get; init; }
	public bool IsMultiFile { get; init; }
	public int DisplayOffset { get; set; }
}

