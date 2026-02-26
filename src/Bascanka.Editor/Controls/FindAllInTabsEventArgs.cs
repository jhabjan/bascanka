using Bascanka.Core.Search;

namespace Bascanka.Editor.Controls;

/// <summary>
/// Event arguments for the Find All in Tabs operation, carrying the search options.
/// </summary>
public sealed class FindAllInTabsEventArgs(SearchOptions options) : EventArgs
{
    /// <summary>The search options to use for the multi-tab search.</summary>
    public SearchOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));
}
