using Bascanka.Core.Buffer;
using Bascanka.Core.IO;

namespace Bascanka.App;


/// <summary>
/// A non-owning wrapper around a <see cref="MemoryMappedFileSource"/> that
/// delegates all <see cref="ITextSource"/> and <see cref="IPrecomputedLineFeeds"/>
/// operations but does NOT implement <see cref="IDisposable"/>.  Used during
/// incremental loading so that intermediate <see cref="PieceTable"/> instances
/// can be disposed without releasing the shared underlying source.
/// </summary>
internal sealed class BorrowedTextSource(MemoryMappedFileSource inner) : ITextSource, IPrecomputedLineFeeds
{
	private readonly MemoryMappedFileSource _inner = inner;

	public char this[long index] => _inner[index];
	public long Length => _inner.Length;
	public string GetText(long start, long length) => _inner.GetText(start, length);
	public int CountLineFeeds(long start, long length) => _inner.CountLineFeeds(start, length);
	public int InitialLineFeedCount => _inner.InitialLineFeedCount;
	public long[]? LineOffsets => _inner.LineOffsets;
}

