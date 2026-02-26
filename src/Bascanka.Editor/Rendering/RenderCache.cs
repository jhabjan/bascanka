using System.Drawing;

namespace Bascanka.Editor.Rendering;

/// <summary>
/// An LRU bitmap cache that stores pre-rendered line images to avoid
/// re-drawing unchanged lines every paint cycle.
/// </summary>
/// <remarks>
/// <para>
/// The cache keeps at most <see cref="MaxCachedLines"/> entries (default:
/// three times the visible line count, covering one screen above and one
/// below for smooth scrolling).  When the limit is reached, the least
/// recently used entry is evicted and its <see cref="Bitmap"/> is disposed.
/// </para>
/// <para>
/// Call <see cref="Invalidate"/> or <see cref="InvalidateRange"/> when
/// a line is edited, and <see cref="InvalidateAll"/> when the theme, font,
/// or viewport width changes.
/// </para>
/// <para>This class is <b>not</b> thread-safe; callers must synchronize
/// externally if they access it from multiple threads.</para>
/// </remarks>
public sealed class RenderCache : IDisposable
{

    private readonly Dictionary<long, CacheEntry> _map = [];
    private readonly LinkedList<CacheEntry> _lruList = new();
    private bool _disposed;

    // ── Capacity ──────────────────────────────────────────────────────

    private int _maxCachedLines = 150; // sensible default; recalculated from visible lines

    /// <summary>
    /// Maximum number of line bitmaps kept in the cache.
    /// When the count exceeds this value, the least recently used entries
    /// are evicted.
    /// </summary>
    public int MaxCachedLines
    {
        get => _maxCachedLines;
        set => _maxCachedLines = Math.Max(1, value);
    }

    /// <summary>
    /// Adjusts <see cref="MaxCachedLines"/> to three times the given visible
    /// line count (one screen above + current screen + one screen below).
    /// </summary>
    public void SetVisibleLineCount(int visibleLines)
    {
        MaxCachedLines = Math.Max(1, visibleLines) * 3;
    }

    // ── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Returns a cached bitmap for the given <paramref name="line"/>, or renders
    /// one via <paramref name="renderFunc"/>, caches it, and returns it.
    /// </summary>
    /// <param name="line">Zero-based line index.</param>
    /// <param name="renderFunc">
    /// A factory that creates the <see cref="Bitmap"/> for the line.
    /// Called only on a cache miss.
    /// </param>
    public Bitmap GetOrRender(long line, Func<Bitmap> renderFunc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderFunc);

        if (_map.TryGetValue(line, out var entry))
        {
            // Cache hit -- promote to most-recently-used.
            Promote(entry);
            return entry.Bitmap;
        }

        // Cache miss -- render, store, and possibly evict.
        Bitmap bitmap = renderFunc();
        entry = new CacheEntry(line, bitmap);
        entry.Node = _lruList.AddFirst(entry);
        _map[line] = entry;

        Evict();

        return bitmap;
    }

    /// <summary>
    /// Invalidates a single cached line, disposing its bitmap.
    /// </summary>
    public void Invalidate(long line)
    {
        if (_map.TryGetValue(line, out var entry))
        {
            Remove(entry);
        }
    }

    /// <summary>
    /// Invalidates all cached lines in the half-open range
    /// [<paramref name="startLine"/>, <paramref name="endLine"/>).
    /// </summary>
    public void InvalidateRange(long startLine, long endLine)
    {
        // Collect keys first to avoid modifying the dictionary during enumeration.
        var toRemove = new List<long>();
        foreach (long key in _map.Keys)
        {
            if (key >= startLine && key < endLine)
                toRemove.Add(key);
        }

        foreach (long key in toRemove)
        {
            if (_map.TryGetValue(key, out var entry))
                Remove(entry);
        }
    }

    /// <summary>
    /// Invalidates the entire cache, disposing all bitmaps.
    /// Call this when the theme, font, or viewport width changes.
    /// </summary>
    public void InvalidateAll()
    {
        foreach (var entry in _map.Values)
        {
            entry.Bitmap.Dispose();
        }
        _map.Clear();
        _lruList.Clear();
    }

    /// <summary>
    /// Returns the number of entries currently in the cache.
    /// </summary>
    public int Count => _map.Count;

    // ── IDisposable ───────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        InvalidateAll();
    }

    // ── Private helpers ───────────────────────────────────────────────

    /// <summary>
    /// Moves an entry to the front (most-recently-used position) of the LRU list.
    /// </summary>
    private void Promote(CacheEntry entry)
    {
        if (entry.Node is not null && entry.Node != _lruList.First)
        {
            _lruList.Remove(entry.Node);
            entry.Node = _lruList.AddFirst(entry);
        }
    }

    /// <summary>
    /// Removes and disposes a single cache entry.
    /// </summary>
    private void Remove(CacheEntry entry)
    {
        if (entry.Node is not null)
            _lruList.Remove(entry.Node);
        _map.Remove(entry.Line);
        entry.Bitmap.Dispose();
    }

    /// <summary>
    /// Evicts the least-recently-used entries until the cache is at or below capacity.
    /// </summary>
    private void Evict()
    {
        while (_map.Count > _maxCachedLines && _lruList.Last is not null)
        {
            var victim = _lruList.Last.Value;
            Remove(victim);
        }
    }
}
