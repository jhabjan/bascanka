using System.IO.MemoryMappedFiles;
using System.Text;
using Bascanka.Core.Buffer;
using TextEncoding = System.Text.Encoding;

namespace Bascanka.Core.IO;

/// <summary>
/// An <see cref="ITextSource"/> implementation backed by a read-only
/// <see cref="MemoryMappedFile"/>.  Designed for large files that should not
/// be loaded entirely into managed memory.  Decoded text is served through
/// an internal <see cref="ChunkCache"/> that holds up to 4 MB of recently
/// accessed text.
/// <para>
/// Supports two construction modes:
/// <list type="bullet">
///   <item><b>Eager</b> (default) — scans the entire file during construction.</item>
///   <item><b>Deferred</b> (<c>deferScan: true</c>) — sets up the memory map
///     instantly; call <see cref="ScanNextBatch"/> repeatedly to process chunks
///     incrementally.  <see cref="Length"/>, <see cref="InitialLineFeedCount"/>,
///     and <see cref="LineOffsets"/> grow with each batch.</item>
/// </list>
/// </para>
/// </summary>
public sealed class MemoryMappedFileSource : ITextSource, IPrecomputedLineFeeds, IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly ChunkCache _cache;
    private readonly string _detectedLineEnding;

    /// <summary>
    /// Chunk directory: <c>_chunkCharOffsets[i]</c> is the cumulative character
    /// count of all chunks before chunk <c>i</c>.  Used for O(log N) binary
    /// search to map a character offset to the correct chunk.
    /// </summary>
    private readonly long[] _chunkCharOffsets;

    /// <summary>Total number of chunks in the file.</summary>
    private readonly int _chunkCount;

    // ── Scan state (mutable during incremental scan) ─────────────────
    private long _charLength;
    private int _cachedTotalLineFeeds;
    private int _scannedChunks;
    private List<long>? _lineOffsetBuilder;
    private long[]? _lineOffsets;
    private bool _disposed;

    /// <summary>Full path to the file on disk.</summary>
    public string FilePath { get; }

    /// <summary>Size of the file in bytes.</summary>
    public long FileSize { get; }

    /// <summary>The encoding used to decode the file.</summary>
    public TextEncoding Encoding { get; }

    /// <summary>
    /// The total number of <c>'\n'</c> characters scanned so far.
    /// Grows during incremental scanning.
    /// </summary>
    public int InitialLineFeedCount => _cachedTotalLineFeeds;

    /// <inheritdoc />
    public long[]? LineOffsets => _lineOffsets;

    /// <summary>
    /// The detected dominant line ending style from the first 64 KB of the raw file.
    /// Returns <c>"CRLF"</c>, <c>"LF"</c>, or <c>"CR"</c>.
    /// </summary>
    public string DetectedLineEnding => _detectedLineEnding;

    /// <summary>
    /// Whether the full file has been scanned.
    /// Always <see langword="true"/> after eager construction.
    /// </summary>
    public bool IsFullyScanned => _scannedChunks >= _chunkCount;

    /// <summary>
    /// Approximate number of bytes scanned so far (chunks × chunk size, clamped to file size).
    /// </summary>
    public long ScannedBytes => Math.Min((long)_scannedChunks * ChunkCache.ChunkSizeBytes, FileSize);

    /// <summary>
    /// Opens the specified file as a read-only memory-mapped file, detects its
    /// encoding, and prepares the chunk cache.
    /// </summary>
    /// <param name="filePath">Absolute path to the file.</param>
    /// <param name="encoding">
    /// Optional encoding override.  When <see langword="null"/> the encoding
    /// is detected automatically from the file's BOM or content heuristics.
    /// </param>
    /// <param name="normalizeLineEndings">
    /// When <see langword="true"/>, line endings are normalized to <c>\n</c>
    /// inside each decoded chunk.
    /// </param>
    /// <param name="deferScan">
    /// When <see langword="true"/>, the constructor returns immediately without
    /// scanning the file.  Call <see cref="ScanNextBatch"/> to process chunks
    /// incrementally.
    /// </param>
    public MemoryMappedFileSource(string filePath, TextEncoding? encoding = null,
        bool normalizeLineEndings = false, bool deferScan = false)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found.", filePath);

        FilePath = Path.GetFullPath(filePath);
        FileSize = new FileInfo(FilePath).Length;

        // Detect encoding if not provided.
        if (encoding is null)
        {
            using FileStream detectStream = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Encoding = EncodingDetector.DetectEncoding(detectStream);
        }
        else
        {
            Encoding = encoding;
        }

        // A zero-length file cannot be memory-mapped on Windows (CreateFileMapping
        // rejects a zero maxSize).  Handle it as a degenerate case.
        if (FileSize == 0)
        {
            _mmf = null!;
            _cache = null!;
            _charLength = 0;
            _cachedTotalLineFeeds = 0;
            _detectedLineEnding = "LF";
            _chunkCharOffsets = [];
            _chunkCount = 0;
            _scannedChunks = 0;
            _lineOffsets = [0];
            return;
        }

        _mmf = MemoryMappedFile.CreateFromFile(
            FilePath,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read);

        _cache = new ChunkCache(_mmf, FileSize, Encoding, normalizeLineEndings);

        // Detect line ending style from the first chunk's raw bytes.
        _detectedLineEnding = DetectLineEndingFromRawBytes();

        // Compute chunk count.
        _chunkCount = (int)((FileSize + ChunkCache.ChunkSizeBytes - 1) / ChunkCache.ChunkSizeBytes);
        _chunkCharOffsets = new long[_chunkCount];

        if (deferScan)
        {
            // Incremental mode — caller will drive scanning via ScanNextBatch.
            _lineOffsetBuilder = new List<long> { 0 };
        }
        else
        {
            // Eager mode — scan the entire file now.
            _lineOffsetBuilder = new List<long> { 0 };
            ScanNextBatch(_chunkCount);
        }
    }

    /// <inheritdoc />
    public long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _charLength;
        }
    }

    /// <inheritdoc />
    public char this[long index]
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (index < 0 || index >= _charLength)
                throw new ArgumentOutOfRangeException(nameof(index));

            int ci = FindChunkIndex(index);
            string chunk = _cache.GetChunk((long)ci * ChunkCache.ChunkSizeBytes);
            return chunk[(int)(index - _chunkCharOffsets[ci])];
        }
    }

    /// <inheritdoc />
    public string GetText(long start, long length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRange(start, length);

        if (length == 0)
            return string.Empty;

        int ci = FindChunkIndex(start);
        long remaining = length;

        // Fast path: entire range fits in one chunk.
        string firstChunk = _cache.GetChunk((long)ci * ChunkCache.ChunkSizeBytes);
        int localStart = (int)(start - _chunkCharOffsets[ci]);
        int available = firstChunk.Length - localStart;
        if (remaining <= available)
            return firstChunk.Substring(localStart, (int)remaining);

        var sb = new StringBuilder((int)Math.Min(length, int.MaxValue));
        sb.Append(firstChunk, localStart, available);
        remaining -= available;
        ci++;

        while (remaining > 0 && ci < _scannedChunks)
        {
            string chunk = _cache.GetChunk((long)ci * ChunkCache.ChunkSizeBytes);
            int toCopy = (int)Math.Min(chunk.Length, remaining);
            sb.Append(chunk, 0, toCopy);
            remaining -= toCopy;
            ci++;
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public int CountLineFeeds(long start, long length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRange(start, length);

        if (length == 0)
            return 0;

        // Fast path: full-range query returns the pre-computed value.
        if (start == 0 && length == _charLength)
            return _cachedTotalLineFeeds;

        int count = 0;
        int ci = FindChunkIndex(start);
        long remaining = length;

        while (remaining > 0 && ci < _scannedChunks)
        {
            string chunk = _cache.GetChunk((long)ci * ChunkCache.ChunkSizeBytes);
            int localStart = (int)Math.Max(start - _chunkCharOffsets[ci], 0);
            int avail = chunk.Length - localStart;
            int toScan = (int)Math.Min(avail, remaining);

            ReadOnlySpan<char> span = chunk.AsSpan(localStart, toScan);
            foreach (char c in span)
            {
                if (c == '\n')
                    count++;
            }

            remaining -= toScan;
            ci++;
        }

        return count;
    }

    /// <summary>
    /// Scans the next <paramref name="batchSize"/> chunks, updating
    /// <see cref="Length"/>, <see cref="InitialLineFeedCount"/>, and
    /// <see cref="LineOffsets"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the entire file has been scanned.
    /// </returns>
    public bool ScanNextBatch(int batchSize)
    {
        if (_scannedChunks >= _chunkCount)
            return true;

        int endChunk = Math.Min(_scannedChunks + batchSize, _chunkCount);

        long totalChars = _charLength;
        int totalLf = _cachedTotalLineFeeds;

        for (int ci = _scannedChunks; ci < endChunk; ci++)
        {
            _chunkCharOffsets[ci] = totalChars;

            string chunk = _cache.GetChunk((long)ci * ChunkCache.ChunkSizeBytes);

            ReadOnlySpan<char> span = chunk.AsSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] == '\n')
                {
                    totalLf++;
                    _lineOffsetBuilder!.Add(totalChars + i + 1);
                }
            }

            totalChars += chunk.Length;
        }

        // Update visible state.  Order matters: update offsets and counts
        // before advancing _scannedChunks so that FindChunkIndex sees
        // consistent data.
        _charLength = totalChars;
        _cachedTotalLineFeeds = totalLf;
        _lineOffsets = _lineOffsetBuilder!.ToArray();
        _scannedChunks = endChunk;

        bool done = _scannedChunks >= _chunkCount;
        if (done)
            _lineOffsetBuilder = null; // free builder memory

        return done;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cache?.Dispose();
        _mmf?.Dispose();
    }

    /// <summary>
    /// Binary searches the chunk directory to find the chunk index that
    /// contains the given character offset.  Only searches within
    /// <see cref="_scannedChunks"/> to avoid reading uninitialised entries.
    /// </summary>
    private int FindChunkIndex(long charOffset)
    {
        int lo = 0, hi = _scannedChunks - 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (_chunkCharOffsets[mid] <= charOffset)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    /// <summary>
    /// Reads the first chunk's raw bytes to detect the dominant line ending style.
    /// </summary>
    private string DetectLineEndingFromRawBytes()
    {
        long bytesToRead = Math.Min(ChunkCache.ChunkSizeBytes, FileSize);
        if (bytesToRead <= 0) return "LF";

        using var accessor = _mmf.CreateViewAccessor(0, bytesToRead, MemoryMappedFileAccess.Read);
        byte[] buffer = new byte[bytesToRead];
        accessor.ReadArray(0, buffer, 0, (int)bytesToRead);

        int crlfCount = 0, lfCount = 0, crCount = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == 0x0D) // \r
            {
                if (i + 1 < buffer.Length && buffer[i + 1] == 0x0A) // \r\n
                {
                    crlfCount++;
                    i++; // skip the \n
                }
                else
                {
                    crCount++;
                }
            }
            else if (buffer[i] == 0x0A) // \n
            {
                lfCount++;
            }
        }

        if (crlfCount >= lfCount && crlfCount >= crCount) return "CRLF";
        if (lfCount >= crCount) return "LF";
        return "CR";
    }

    private void ValidateRange(long start, long length)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start), "Start must be non-negative.");
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");
        if (start + length > _charLength)
            throw new ArgumentOutOfRangeException(nameof(length), "Range exceeds source length.");
    }
}
