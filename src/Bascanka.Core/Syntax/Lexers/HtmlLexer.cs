namespace Bascanka.Core.Syntax.Lexers;

/// <summary>
/// Lexer for HTML.  Handles tags, attributes, attribute values, entities
/// (<c>&amp;amp;</c>), and HTML comments (<c>&lt;!-- --&gt;</c>).
/// Supports embedded <c>&lt;script&gt;</c> (JavaScript) and <c>&lt;style&gt;</c> (CSS)
/// content by delegating those regions to their respective lexers.
/// </summary>
public sealed class HtmlLexer : BaseLexer
{
    private const int StateInComment = 10;
    private const int StateInScript = 20;
    private const int StateInStyle = 21;

    private const int TagKindNone = 0;
    private const int TagKindScript = 1;
    private const int TagKindStyle = 2;
    private const int TagStateClosingFlag = 0x100;

    private static readonly JavaScriptLexer JsLexer = new();
    private static readonly CssLexer CssLexer = new();

    public override string LanguageId => "html";
    public override string[] FileExtensions => [".html", ".htm", ".xhtml", ".shtml"];

    protected override LexerState TokenizeNormal(
        string line, ref int pos, List<Token> tokens, LexerState state)
    {
        if (SkipWhitespace(line, ref pos, tokens))
            return state;

        char c = line[pos];

        // HTML comment start.
        if (StartsWith(line, pos, "<!--"))
        {
            return ReadHtmlComment(line, ref pos, tokens);
        }

        // DOCTYPE.
        if (StartsWith(line, pos, "<!"))
        {
            int start = pos;
            pos += 2;
            while (pos < line.Length && line[pos] != '>')
                pos++;
            if (pos < line.Length) pos++; // skip >
            tokens.Add(new Token(start, pos - start, TokenType.Tag));
            return state;
        }

        // Tag open: < or </
        if (c == '<')
        {
            return ReadTag(line, ref pos, tokens);
        }

        // Entity.
        if (c == '&')
        {
            int start = pos;
            pos++;
            while (pos < line.Length && line[pos] != ';' && !char.IsWhiteSpace(line[pos]) && (pos - start) < 12)
                pos++;
            if (pos < line.Length && line[pos] == ';')
                pos++;
            tokens.Add(new Token(start, pos - start, TokenType.Entity));
            return state;
        }

        // Plain text between tags.
        int textStart = pos;
        while (pos < line.Length && line[pos] != '<' && line[pos] != '&')
            pos++;

        if (pos > textStart)
            tokens.Add(new Token(textStart, pos - textStart, TokenType.Plain));

        return state;
    }

    protected override LexerState ContinueMultiLineState(
        string line, ref int pos, List<Token> tokens, LexerState state)
    {
        return state.StateId switch
        {
            StateInComment => ContinueHtmlComment(line, ref pos, tokens),
            LexerState.StateInTag => ContinueTag(line, ref pos, tokens, state.NestingDepth),
            StateInScript => ContinueScript(line, ref pos, tokens, state),
            StateInStyle => ContinueStyle(line, ref pos, tokens, state),
            _ => state,
        };
    }

    // ── HTML comments ───────────────────────────────────────────────────

    private static LexerState ReadHtmlComment(string line, ref int pos, List<Token> tokens)
    {
        int start = pos;
        pos += 4; // skip <!--

        int closeIdx = line.IndexOf("-->", pos, StringComparison.Ordinal);
        if (closeIdx >= 0)
        {
            pos = closeIdx + 3;
            tokens.Add(new Token(start, pos - start, TokenType.MultiLineComment));
            return LexerState.Normal;
        }

        tokens.Add(new Token(start, line.Length - start, TokenType.MultiLineComment));
        pos = line.Length;
        return new LexerState(StateInComment, 0);
    }

    private static LexerState ContinueHtmlComment(string line, ref int pos, List<Token> tokens)
    {
        int start = pos;
        int closeIdx = line.IndexOf("-->", pos, StringComparison.Ordinal);
        if (closeIdx >= 0)
        {
            pos = closeIdx + 3;
            tokens.Add(new Token(start, pos - start, TokenType.MultiLineComment));
            return LexerState.Normal;
        }

        tokens.Add(new Token(start, line.Length - start, TokenType.MultiLineComment));
        pos = line.Length;
        return new LexerState(StateInComment, 0);
    }

    // ── Tags ────────────────────────────────────────────────────────────

    private static LexerState ReadTag(string line, ref int pos, List<Token> tokens)
    {
        int start = pos;
        pos++; // skip <

        // Closing tag?
        bool isClosing = false;
        if (pos < line.Length && line[pos] == '/')
        {
            pos++;
            isClosing = true;
        }

        // Read tag name.
        int nameStart = pos;
        while (pos < line.Length && (IsIdentPart(line[pos]) || line[pos] == '-' || line[pos] == ':'))
            pos++;

        tokens.Add(new Token(start, pos - start, TokenType.Tag));

        // Read attributes until > or end of line.
        string tagName = line.Substring(nameStart, pos - nameStart);
        int tagKind = GetTagKind(tagName);
        int tagState = tagKind | (isClosing ? TagStateClosingFlag : 0);
        return ReadAttributes(line, ref pos, tokens, tagState);
    }

    private static LexerState ReadAttributes(string line, ref int pos, List<Token> tokens, int tagState)
    {
        while (pos < line.Length)
        {
            // Skip whitespace.
            if (char.IsWhiteSpace(line[pos]))
            {
                int ws = pos;
                while (pos < line.Length && char.IsWhiteSpace(line[pos]))
                    pos++;
                tokens.Add(new Token(ws, pos - ws, TokenType.Plain));
                continue;
            }

            // Self-closing or closing.
            if (line[pos] == '/' && pos + 1 < line.Length && line[pos + 1] == '>')
            {
                EmitPunctuation(line, ref pos, tokens, 2);
                return NextStateAfterTagClose(tagState);
            }

            if (line[pos] == '>')
            {
                EmitPunctuation(line, ref pos, tokens);
                return NextStateAfterTagClose(tagState);
            }

            // Attribute name.
            if (IsIdentStart(line[pos]) || line[pos] == '-' || line[pos] == ':' || line[pos] == '@' || line[pos] == '*')
            {
                int attrStart = pos;
                while (pos < line.Length && (IsIdentPart(line[pos]) || line[pos] == '-' || line[pos] == ':' || line[pos] == '@' || line[pos] == '*'))
                    pos++;
                tokens.Add(new Token(attrStart, pos - attrStart, TokenType.TagAttribute));

                // Skip = and optional whitespace.
                if (pos < line.Length && line[pos] == '=')
                {
                    EmitOperator(line, ref pos, tokens);

                    while (pos < line.Length && char.IsWhiteSpace(line[pos]))
                        pos++;

                    // Attribute value.
                    if (pos < line.Length && (line[pos] == '"' || line[pos] == '\''))
                    {
                        ReadAttrValue(line, ref pos, tokens, line[pos]);
                    }
                    else if (pos < line.Length)
                    {
                        // Unquoted value.
                        int valStart = pos;
                        while (pos < line.Length && !char.IsWhiteSpace(line[pos]) && line[pos] != '>')
                            pos++;
                        tokens.Add(new Token(valStart, pos - valStart, TokenType.TagAttributeValue));
                    }
                }
                continue;
            }

            // Unknown character in tag context.
            tokens.Add(new Token(pos, 1, TokenType.Plain));
            pos++;
        }

        // Tag not closed on this line.
        return new LexerState(LexerState.StateInTag, tagState);
    }

    private static LexerState ContinueTag(string line, ref int pos, List<Token> tokens, int tagState)
    {
        return ReadAttributes(line, ref pos, tokens, tagState);
    }

    private static void ReadAttrValue(string line, ref int pos, List<Token> tokens, char quote)
    {
        int start = pos;
        pos++; // skip opening quote

        while (pos < line.Length && line[pos] != quote)
            pos++;

        if (pos < line.Length)
            pos++; // skip closing quote

        tokens.Add(new Token(start, pos - start, TokenType.TagAttributeValue));
    }

    private static LexerState ContinueScript(string line, ref int pos, List<Token> tokens, LexerState state)
    {
        return ContinueEmbedded(line, ref pos, tokens, state, "</script", JsLexer, StateInScript);
    }

    private static LexerState ContinueStyle(string line, ref int pos, List<Token> tokens, LexerState state)
    {
        return ContinueEmbedded(line, ref pos, tokens, state, "</style", CssLexer, StateInStyle);
    }

    private static LexerState ContinueEmbedded(
        string line, ref int pos, List<Token> tokens, LexerState state,
        string closeTag, ILexer lexer, int modeStateId)
    {
        int closeIdx = line.IndexOf(closeTag, pos, StringComparison.OrdinalIgnoreCase);
        int end = closeIdx >= 0 ? closeIdx : line.Length;

        var subState = UnpackEmbeddedState(state.NestingDepth);
        if (end > pos)
        {
            var (subTokens, endState) = lexer.Tokenize(line.Substring(pos, end - pos), subState);
            foreach (var t in subTokens)
                tokens.Add(new Token(t.Start + pos, t.Length, t.Type));
            subState = endState;
        }

        if (closeIdx >= 0)
        {
            pos = closeIdx;
            return LexerState.Normal;
        }

        pos = line.Length;
        return new LexerState(modeStateId, PackEmbeddedState(subState));
    }

    private static int GetTagKind(string tagName)
    {
        if (string.Equals(tagName, "script", StringComparison.OrdinalIgnoreCase))
            return TagKindScript;
        if (string.Equals(tagName, "style", StringComparison.OrdinalIgnoreCase))
            return TagKindStyle;
        return TagKindNone;
    }

    private static LexerState NextStateAfterTagClose(int tagState)
    {
        bool isClosing = (tagState & TagStateClosingFlag) != 0;
        int tagKind = tagState & 0xFF;

        if (isClosing)
            return LexerState.Normal;

        return tagKind switch
        {
            TagKindScript => new LexerState(StateInScript, PackEmbeddedState(LexerState.Normal)),
            TagKindStyle => new LexerState(StateInStyle, PackEmbeddedState(LexerState.Normal)),
            _ => LexerState.Normal,
        };
    }

    private static int PackEmbeddedState(LexerState state)
    {
        int stateId = state.StateId & 0xFFFF;
        int depth = state.NestingDepth & 0xFFFF;
        return stateId | (depth << 16);
    }

    private static LexerState UnpackEmbeddedState(int packed)
    {
        int stateId = packed & 0xFFFF;
        int depth = (packed >> 16) & 0xFFFF;
        return new LexerState(stateId, depth);
    }
}
