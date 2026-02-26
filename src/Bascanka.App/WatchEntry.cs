namespace Bascanka.App;

internal sealed class WatchEntry
{
	public required FileSystemWatcher Watcher { get; init; }
	public required string FilePath { get; init; }
	public required System.Windows.Forms.Timer DebounceTimer { get; init; }
	public bool PendingChange { get; set; }
}

