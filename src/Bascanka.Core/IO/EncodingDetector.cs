using TextEncoding = System.Text.Encoding;

namespace Bascanka.Core.IO;

/// <summary>
/// Provides static methods for detecting the character encoding of a byte stream.
/// The detector inspects the first 4 KB for a Byte-Order Mark (BOM) and, when no
/// BOM is found, applies simple heuristics to distinguish common encodings.
/// </summary>
public static class EncodingDetector
{
    /// <summary>Maximum number of bytes to examine for detection.</summary>
    private const int SampleSize = 4096;

    /// <summary>
    /// Detects the encoding of the given <paramref name="stream"/> by examining
    /// up to the first 4 KB.  The stream position is reset to its original
    /// location after detection.
    /// </summary>
    /// <param name="stream">A readable, seekable stream.</param>
    /// <returns>The detected <see cref="System.Text.Encoding"/>.</returns>
    public static TextEncoding DetectEncoding(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));

        long originalPosition = stream.CanSeek ? stream.Position : 0;

        byte[] buffer = new byte[SampleSize];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        if (stream.CanSeek)
            stream.Position = originalPosition;

        if (bytesRead == 0)
            return TextEncoding.UTF8;

        ReadOnlySpan<byte> sample = buffer.AsSpan(0, bytesRead);

        // --- BOM detection ---
        TextEncoding? bomEncoding = DetectBom(sample);
        if (bomEncoding is not null)
            return bomEncoding;

        // --- Heuristic detection ---
        return DetectByHeuristic(sample);
    }

    /// <summary>
    /// Detects the encoding of the given byte array by examining BOM and heuristics.
    /// </summary>
    /// <param name="data">The raw bytes to examine.</param>
    /// <returns>The detected <see cref="System.Text.Encoding"/>.</returns>
    public static TextEncoding DetectEncoding(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return TextEncoding.UTF8;

        TextEncoding? bomEncoding = DetectBom(data);
        if (bomEncoding is not null)
            return bomEncoding;

        return DetectByHeuristic(data);
    }

    /// <summary>
    /// Checks for a Byte-Order Mark at the start of <paramref name="data"/>.
    /// </summary>
    private static TextEncoding? DetectBom(ReadOnlySpan<byte> data)
    {
        // UTF-32 LE BOM: FF FE 00 00  (must check before UTF-16 LE)
        if (data.Length >= 4 &&
            data[0] == 0xFF && data[1] == 0xFE &&
            data[2] == 0x00 && data[3] == 0x00)
        {
            return TextEncoding.UTF32; // UTF-32 LE
        }

        // UTF-32 BE BOM: 00 00 FE FF
        if (data.Length >= 4 &&
            data[0] == 0x00 && data[1] == 0x00 &&
            data[2] == 0xFE && data[3] == 0xFF)
        {
            return new System.Text.UTF32Encoding(bigEndian: true, byteOrderMark: true); // UTF-32 BE
        }

        // UTF-8 BOM: EF BB BF
        if (data.Length >= 3 &&
            data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true); // UTF-8 with BOM
        }

        // UTF-16 LE BOM: FF FE
        if (data.Length >= 2 &&
            data[0] == 0xFF && data[1] == 0xFE)
        {
            return TextEncoding.Unicode; // UTF-16 LE
        }

        // UTF-16 BE BOM: FE FF
        if (data.Length >= 2 &&
            data[0] == 0xFE && data[1] == 0xFF)
        {
            return TextEncoding.BigEndianUnicode; // UTF-16 BE
        }

        return null;
    }

    /// <summary>
    /// When no BOM is present, applies a simple heuristic to distinguish
    /// UTF-16 (via null byte frequency) from UTF-8 / single-byte encodings.
    /// </summary>
    private static TextEncoding DetectByHeuristic(ReadOnlySpan<byte> data)
    {
        int nullCount = 0;
        int oddNulls = 0;   // nulls at odd indices  -> suggests UTF-16 LE
        int evenNulls = 0;  // nulls at even indices -> suggests UTF-16 BE
        int highBitCount = 0;

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0x00)
            {
                nullCount++;
                if (i % 2 == 0)
                    evenNulls++;
                else
                    oddNulls++;
            }
            else if (data[i] >= 0x80)
            {
                highBitCount++;
            }
        }

        // If a significant portion of every other byte is null, it is likely UTF-16.
        double nullRatio = (double)nullCount / data.Length;
        if (nullRatio > 0.2)
        {
            // More nulls in odd positions -> ASCII chars in even positions -> LE
            if (oddNulls > evenNulls)
                return TextEncoding.Unicode; // UTF-16 LE

            // More nulls in even positions -> BE
            return TextEncoding.BigEndianUnicode; // UTF-16 BE
        }

        var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var gb18030 = TextEncoding.GetEncoding("GB18030");

        int utf8Replacements = CountReplacementChars(utf8, data);
        int gbReplacements = CountReplacementChars(gb18030, data);

        // If UTF-8 produces replacement chars but GB18030 does not, prefer GB18030.
        if (utf8Replacements > 0 && gbReplacements < utf8Replacements)
            return gb18030;

        // Try to validate as UTF-8.
        if (IsValidUtf8(data))
            return utf8; // UTF-8 without BOM

        // If GB18030 decodes cleanly, prefer it over a Latin-1 fallback.
        if (gbReplacements == 0)
            return gb18030;

        // Fallback: Windows-1252 (Latin-1 superset, most common single-byte on Windows).
        return TextEncoding.GetEncoding(1252);
    }

    /// <summary>
    /// Validates whether the data is well-formed UTF-8.  Returns <see langword="true"/>
    /// if every byte sequence is valid; <see langword="false"/> if any illegal
    /// sequence is found.  Pure-ASCII data is trivially valid UTF-8.
    /// </summary>
    private static bool IsValidUtf8(ReadOnlySpan<byte> data)
    {
        int i = 0;
        while (i < data.Length)
        {
            byte b = data[i];

            int continuationBytes;
            if (b <= 0x7F)
            {
                // Single-byte (ASCII)
                i++;
                continue;
            }
            else if ((b & 0xE0) == 0xC0)
            {
                continuationBytes = 1;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                continuationBytes = 2;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                continuationBytes = 3;
            }
            else
            {
                // Invalid leading byte.
                return false;
            }

            // Verify continuation bytes.
            if (i + continuationBytes >= data.Length)
                return false;

            for (int j = 1; j <= continuationBytes; j++)
            {
                if ((data[i + j] & 0xC0) != 0x80)
                    return false;
            }

            i += 1 + continuationBytes;
        }

        return true;
    }

    private static int CountReplacementChars(TextEncoding encoding, ReadOnlySpan<byte> data)
    {
        try
        {
            string decoded = encoding.GetString(data);
            int count = 0;
            foreach (char c in decoded)
            {
                if (c == '\uFFFD')
                    count++;
            }
            return count;
        }
        catch
        {
            return int.MaxValue;
        }
    }
}
