using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Bascanka.Core.Search;

/// <summary>
/// Parses sed-style substitution expressions (s/pattern/replacement/flags)
/// and executes them via <see cref="Regex"/>.
/// </summary>
public static class SedCommandParser
{
    /// <summary>
    /// Attempts to parse a sed substitution expression.
    /// Supports any non-alphanumeric delimiter and backslash-escaped delimiters
    /// within pattern/replacement sections.
    /// </summary>
    public static bool TryParse(string input, [NotNullWhen(true)] out SedCommand? command)
    {
        command = null;

        if (input is null || input.Length < 4)
            return false;

        // Must start with 's' followed by a non-alphanumeric delimiter.
        if (input[0] != 's')
            return false;

        char delimiter = input[1];
        if (char.IsLetterOrDigit(delimiter))
            return false;

        // Walk through the string extracting pattern, replacement, and flags.
        // Backslash-escaped delimiters are treated as literal characters.
        int pos = 2;

        // Extract pattern.
        if (!ExtractSection(input, delimiter, ref pos, out string pattern))
            return false;

        // Extract replacement.
        if (!ExtractSection(input, delimiter, ref pos, out string replacement))
            return false;

        // Remaining text is flags (trailing delimiter already consumed).
        string flagStr = pos < input.Length ? input[pos..] : string.Empty;

        bool global = false;
        bool ignoreCase = false;
        foreach (char c in flagStr)
        {
            switch (c)
            {
                case 'g': global = true; break;
                case 'i': ignoreCase = true; break;
                // Unknown flags silently ignored.
            }
        }

        command = new SedCommand
        {
            Pattern = pattern,
            Replacement = replacement,
            Global = global,
            IgnoreCase = ignoreCase,
        };
        return true;
    }

    /// <summary>
    /// Executes a parsed sed command on the given input text.
    /// Returns the transformed text and the number of replacements made.
    /// </summary>
    public static (string Result, int Count) Execute(SedCommand cmd, string input)
    {
        var (result, count, _) = ExecuteWithRanges(cmd, input);
        return (result, count);
    }

    /// <summary>
    /// Executes a parsed sed command and returns the replacement ranges
    /// (offset + length in the output text) for each substitution.
    /// </summary>
    public static (string Result, int Count, List<(int Start, int Length)> Ranges)
        ExecuteWithRanges(SedCommand cmd, string input)
    {
        var options = RegexOptions.Compiled;
        if (cmd.IgnoreCase)
            options |= RegexOptions.IgnoreCase;

        var regex = new Regex(cmd.Pattern, options, TimeSpan.FromSeconds(5));
        var sb = new StringBuilder();
        var ranges = new List<(int Start, int Length)>();
        int lastEnd = 0;
        int count = 0;

        foreach (Match m in regex.Matches(input))
        {
            if (!cmd.Global && count > 0)
                break;

            sb.Append(input, lastEnd, m.Index - lastEnd);
            int replStart = sb.Length;
            string repl = m.Result(cmd.Replacement);
            sb.Append(repl);
            if (repl.Length > 0)
                ranges.Add((replStart, repl.Length));
            lastEnd = m.Index + m.Length;
            count++;
        }

        sb.Append(input, lastEnd, input.Length - lastEnd);
        return (sb.ToString(), count, ranges);
    }

    private static bool ExtractSection(string input, char delimiter, ref int pos, out string section)
    {
        var sb = new StringBuilder();
        while (pos < input.Length)
        {
            char c = input[pos];
            if (c == '\\' && pos + 1 < input.Length)
            {
                char next = input[pos + 1];
                if (next == delimiter)
                {
                    // Escaped delimiter — include the literal delimiter.
                    sb.Append(delimiter);
                    pos += 2;
                    continue;
                }
                // Other backslash sequences are preserved as-is for regex.
                sb.Append(c);
                sb.Append(next);
                pos += 2;
                continue;
            }

            if (c == delimiter)
            {
                pos++; // Skip the closing delimiter.
                section = sb.ToString();
                return true;
            }

            sb.Append(c);
            pos++;
        }

        section = string.Empty;
        return false;
    }
}
