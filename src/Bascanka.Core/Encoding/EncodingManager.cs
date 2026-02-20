using System.Text;

namespace Bascanka.Core.Encoding;

/// <summary>
/// Manages the character encoding associated with a document, providing access
/// to the current encoding, BOM presence, and the set of encodings the editor
/// supports.  Also offers a helper for converting raw byte data between encodings.
/// </summary>
public sealed class EncodingManager
{
    /// <summary>
    /// Lazily-built list of encodings the editor supports.  Registered once and
    /// shared across all <see cref="EncodingManager"/> instances.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<System.Text.Encoding>> _supportedEncodings = new(BuildSupportedEncodings);

    private System.Text.Encoding _currentEncoding;

    /// <summary>
    /// Creates a new <see cref="EncodingManager"/> with the given initial encoding.
    /// </summary>
    /// <param name="encoding">
    /// The encoding to use.  Defaults to UTF-8 (no BOM) when <see langword="null"/>.
    /// </param>
    /// <param name="hasBom">
    /// Whether the document was opened with a Byte-Order Mark.
    /// </param>
    public EncodingManager(System.Text.Encoding? encoding = null, bool hasBom = false)
    {
        _currentEncoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        HasBom = hasBom;
    }

    /// <summary>
    /// The encoding currently associated with the document.
    /// </summary>
    public System.Text.Encoding CurrentEncoding
    {
        get => _currentEncoding;
        set => _currentEncoding = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Indicates whether the document's encoding includes a Byte-Order Mark.
    /// </summary>
    public bool HasBom { get; set; }

    /// <summary>
    /// The set of encodings that the editor explicitly supports for open/save
    /// operations.
    /// </summary>
    public static IReadOnlyList<System.Text.Encoding> SupportedEncodings => _supportedEncodings.Value;

    /// <summary>
    /// Transcodes <paramref name="data"/> from <see cref="CurrentEncoding"/>
    /// to <paramref name="target"/>, updates <see cref="CurrentEncoding"/>,
    /// and returns the converted bytes.
    /// </summary>
    /// <param name="data">
    /// The raw bytes encoded in <see cref="CurrentEncoding"/>.
    /// </param>
    /// <param name="target">The encoding to convert to.</param>
    /// <returns>A new byte array in the <paramref name="target"/> encoding.</returns>
    public byte[] ConvertTo(byte[] data, System.Text.Encoding target)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(target);

        // Decode from current encoding to characters, then re-encode.
        string text = _currentEncoding.GetString(data);
        byte[] converted = target.GetBytes(text);

        _currentEncoding = target;
        return converted;
    }

    /// <summary>
    /// Transcodes the given <paramref name="text"/> to the
    /// <paramref name="target"/> encoding and returns the resulting bytes.
    /// Updates <see cref="CurrentEncoding"/> to <paramref name="target"/>.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="target">The target encoding.</param>
    /// <returns>Encoded bytes in the target encoding.</returns>
    public byte[] ConvertTo(string text, System.Text.Encoding target)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(target);

        byte[] converted = target.GetBytes(text);
        _currentEncoding = target;
        return converted;
    }

    /// <summary>
    /// Returns the BOM bytes for <see cref="CurrentEncoding"/>, or an empty
    /// array if <see cref="HasBom"/> is <see langword="false"/>.
    /// </summary>
    public byte[] GetBomBytes()
    {
        if (!HasBom) return [];
        return _currentEncoding.GetPreamble();
    }

    /// <summary>
    /// Builds the canonical list of supported encodings.  Called once via
    /// <see cref="Lazy{T}"/>.
    /// </summary>
    private static IReadOnlyList<System.Text.Encoding> BuildSupportedEncodings()
    {
        return new List<System.Text.Encoding>
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),   // UTF-8
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),    // UTF-8 with BOM
            System.Text.Encoding.Unicode,                                // UTF-16 LE
            System.Text.Encoding.BigEndianUnicode,                       // UTF-16 BE
            System.Text.Encoding.UTF32,                                  // UTF-32 LE
            new UTF32Encoding(bigEndian: true, byteOrderMark: true),    // UTF-32 BE
            System.Text.Encoding.ASCII,                                  // US-ASCII
            System.Text.Encoding.GetEncoding(1252),                      // Windows-1252
            System.Text.Encoding.GetEncoding("iso-8859-1"),              // ISO 8859-1 (Latin-1)
            System.Text.Encoding.GetEncoding("iso-8859-2"),              // ISO 8859-2 (Latin-2)
            System.Text.Encoding.GetEncoding("iso-8859-15"),             // ISO 8859-15 (Latin-9)
            System.Text.Encoding.GetEncoding(932),                       // Shift JIS
            System.Text.Encoding.GetEncoding(936),                       // GBK / GB2312
            System.Text.Encoding.GetEncoding(949),                       // EUC-KR
            System.Text.Encoding.GetEncoding(950),                       // Big5
        }.AsReadOnly();
    }
}
