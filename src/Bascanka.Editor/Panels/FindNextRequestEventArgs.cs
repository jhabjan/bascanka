using Bascanka.Core.Search;

namespace Bascanka.Editor.Panels;

/// <summary>
/// Event arguments for a Find Next/Previous request that should run
/// on a background thread with progress overlay.
/// </summary>
public sealed class FindNextRequestEventArgs(SearchOptions options, long startOffset) : EventArgs
{
	public SearchOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));
	public long StartOffset { get; } = startOffset;
}
