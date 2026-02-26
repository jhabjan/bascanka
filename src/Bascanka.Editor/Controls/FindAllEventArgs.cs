using Bascanka.Core.Search;

namespace Bascanka.Editor.Controls;

/// <summary>
/// Event arguments for the Find All operation, carrying the search options
/// so that MainForm can run the search with progress reporting.
/// </summary>
public sealed class FindAllEventArgs(string searchPattern, SearchOptions options) : EventArgs
{
	/// <summary>The search pattern that was used.</summary>
	public string SearchPattern { get; } = searchPattern;

	/// <summary>The search options to use.</summary>
	public SearchOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));
}
