using Bascanka.Core.Search;

namespace Bascanka.Editor.Panels;

/// <summary>
/// Event arguments for navigating to a search result when the user
/// double-clicks a result row.
/// </summary>
public sealed class NavigateToResultEventArgs(SearchResult result) : EventArgs
{
	/// <summary>The search result the user wants to open.</summary>
	public SearchResult Result { get; } = result ?? throw new ArgumentNullException(nameof(result));
}
