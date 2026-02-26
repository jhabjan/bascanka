namespace Bascanka.Editor.Rendering;


// ── LRU bookkeeping ───────────────────────────────────────────────

/// <summary>
/// An entry in the LRU cache, stored in a doubly-linked list so that
/// promotion and eviction are O(1).
/// </summary>
internal sealed class CacheEntry(long line, Bitmap bitmap)
{
	public long Line = line;
	public Bitmap Bitmap = bitmap;
	public LinkedListNode<CacheEntry>? Node;
}

