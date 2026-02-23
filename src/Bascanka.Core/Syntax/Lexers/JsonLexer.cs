namespace Bascanka.Core.Syntax.Lexers;

/// <summary>
/// Lexer for JSON.  Handles strings (with escape sequences), numbers,
/// boolean/null keywords, and structural punctuation.
/// </summary>
public sealed class JsonLexer : BaseLexer
{
    public override string LanguageId => "json";
    public override string[] FileExtensions => [".json", ".jsonc", ".jsonl"];

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "true", "false", "null",
    };

    protected override LexerState TokenizeNormal(
        string line, ref int pos, List<Token> tokens, LexerState state)
    {
        if (SkipWhitespace(line, ref pos, tokens))
            return state;

        char c = line[pos];

        // Strings.
        if (c == '"')
        {
            int start = pos;
            ReadJsonString(line, ref pos);

            int scan = pos;
            while (scan < line.Length && char.IsWhiteSpace(line[scan]))
                scan++;

            bool isKey = scan < line.Length && line[scan] == ':';
            tokens.Add(new Token(start, pos - start, isKey ? TokenType.JsonKey : TokenType.JsonString));
            return state;
        }

        // Numbers.
        if (char.IsDigit(c) || (c == '-' && pos + 1 < line.Length && char.IsDigit(line[pos + 1])))
        {
            ReadNumber(line, ref pos, tokens);
            return state;
        }

        // Keywords: true, false, null.
        if (IsIdentStart(c))
        {
            if (MatchKeyword(line, pos, Keywords, out int len))
            {
                tokens.Add(new Token(pos, len, TokenType.Keyword));
                pos += len;
            }
            else
            {
                ReadIdentifier(line, ref pos, tokens);
            }
            return state;
        }

        // Line comments (JSONC).
        if (c == '/' && pos + 1 < line.Length)
        {
            if (line[pos + 1] == '/')
            {
                ReadLineComment(line, ref pos, tokens);
                return state;
            }
            if (line[pos + 1] == '*')
            {
                return ReadBlockComment(line, ref pos, tokens, state);
            }
        }

        // Structural punctuation.
        if (c == '{' || c == '}' || c == '[' || c == ']' || c == ',' || c == ':')
        {
            EmitPunctuation(line, ref pos, tokens);
            return state;
        }

        // Fallback: single character as plain.
        tokens.Add(new Token(pos, 1, TokenType.Plain));
        pos++;
        return state;
    }

    protected override LexerState ContinueMultiLineState(
        string line, ref int pos, List<Token> tokens, LexerState state)
    {
        if (state.StateId == LexerState.StateInMultiLineComment)
        {
            return ReadBlockComment(line, ref pos, tokens, state);
        }
        return state;
    }

    private static void ReadJsonString(string line, ref int pos)
    {
        pos++; // skip opening quote

        while (pos < line.Length)
        {
            if (line[pos] == '\\' && pos + 1 < line.Length)
            {
                pos += 2;
            }
            else if (line[pos] == '"')
            {
                pos++;
                break;
            }
            else
            {
                pos++;
            }
        }

    }
}
