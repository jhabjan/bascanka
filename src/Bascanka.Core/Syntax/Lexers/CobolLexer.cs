namespace Bascanka.Core.Syntax.Lexers;

/// <summary>
/// Lexer for COBOL.  Handles keywords (case-insensitive), strings (<c>"..."</c>
/// and <c>'...'</c>), comments (<c>*&gt;</c> inline and column-7 <c>*</c>),
/// PICTURE clauses, level numbers, and standard operators.
/// </summary>
public sealed class CobolLexer : BaseLexer
{
    public override string LanguageId => "COBOL";
    public override string[] FileExtensions => [".cbl", ".cob", ".cpy", ".cobol"];

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Divisions and sections
        "IDENTIFICATION", "ENVIRONMENT", "DATA", "PROCEDURE",
        "DIVISION", "SECTION", "PARAGRAPH",
        "CONFIGURATION", "INPUT-OUTPUT", "FILE-CONTROL",
        "WORKING-STORAGE", "LOCAL-STORAGE", "LINKAGE",
        "SCREEN", "REPORT", "COMMUNICATION",

        // File descriptions
        "FD", "SD", "SELECT", "ASSIGN", "ORGANIZATION",
        "ACCESS", "RECORD", "BLOCK", "LABEL", "STANDARD",
        "OMITTED", "RECORDING", "MODE", "STATUS",

        // Data description clauses
        "PIC", "PICTURE", "VALUE", "VALUES",
        "REDEFINES", "RENAMES", "OCCURS", "DEPENDING",
        "ASCENDING", "DESCENDING", "KEY", "INDEXED",
        "FILLER", "COPY", "REPLACING", "THROUGH", "THRU",
        "JUSTIFIED", "JUST", "BLANK", "WHEN", "ZERO",
        "SIGN", "LEADING", "TRAILING", "SEPARATE",
        "SYNCHRONIZED", "SYNC", "GLOBAL", "EXTERNAL",

        // Verbs
        "ACCEPT", "ADD", "ALTER", "CALL", "CANCEL",
        "CLOSE", "COMPUTE", "CONTINUE", "DELETE",
        "DISPLAY", "DIVIDE", "ENTRY", "EVALUATE",
        "EXIT", "GENERATE", "GO", "GOBACK",
        "IF", "ELSE", "END-IF",
        "INITIALIZE", "INITIATE", "INSPECT",
        "MERGE", "MOVE", "MULTIPLY",
        "OPEN", "PERFORM", "READ", "RECEIVE",
        "RELEASE", "RETURN", "REWRITE",
        "SEARCH", "SEND", "SET", "SORT",
        "START", "STOP", "STRING", "SUBTRACT",
        "SUPPRESS", "TERMINATE", "UNSTRING",
        "USE", "WRITE",

        // Scope terminators
        "END-ADD", "END-CALL", "END-COMPUTE", "END-DELETE",
        "END-DIVIDE", "END-EVALUATE", "END-IF",
        "END-MULTIPLY", "END-PERFORM", "END-READ",
        "END-RETURN", "END-REWRITE", "END-SEARCH",
        "END-START", "END-STRING", "END-SUBTRACT",
        "END-UNSTRING", "END-WRITE", "END-EXEC",

        // Control flow
        "THEN", "NOT", "AND", "OR",
        "UNTIL", "VARYING", "TIMES", "ALSO",
        "WITH", "TEST", "BEFORE", "AFTER",
        "GIVING", "REMAINDER", "ROUNDED",
        "ON", "SIZE", "ERROR", "OVERFLOW",
        "INVALID", "AT", "END", "INTO",
        "TALLYING", "ALL", "CONVERTING",
        "DELIMITED", "BY", "POINTER",
        "COUNT", "CHARACTERS", "SPACES", "SPACE",
        "ZEROS", "ZEROES", "HIGH-VALUES", "LOW-VALUES",
        "QUOTES", "QUOTE",

        // I/O modes
        "INPUT", "OUTPUT", "EXTEND", "I-O",
        "SEQUENTIAL", "RANDOM", "DYNAMIC", "RELATIVE",
        "LINE", "ADVANCING", "PAGE",

        // Special
        "EXEC", "END-EXEC", "SQL", "CICS",
        "PROGRAM-ID", "AUTHOR", "DATE-WRITTEN",
        "DATE-COMPILED", "SECURITY", "INSTALLATION",
        "SOURCE-COMPUTER", "OBJECT-COMPUTER",
        "SPECIAL-NAMES", "REPOSITORY",
        "CLASS", "METHOD", "FACTORY", "OBJECT",
        "INVOKE", "SELF", "SUPER",
        "RAISE", "RESUME",
        "TRUE", "FALSE",
        "CORRESPONDING", "CORR",
        "FROM", "TO", "OF", "IN", "IS", "ARE",
        "THAN", "EQUAL", "GREATER", "LESS",
        "POSITIVE", "NEGATIVE", "NUMERIC",
        "ALPHABETIC", "ALPHABETIC-LOWER", "ALPHABETIC-UPPER",
        "REFERENCE", "CONTENT",
        "OPTIONAL", "RETURNING", "USING",
        "RUN",
    };

    private static readonly HashSet<string> TypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BINARY", "COMP", "COMP-1", "COMP-2", "COMP-3", "COMP-4", "COMP-5",
        "COMPUTATIONAL", "COMPUTATIONAL-1", "COMPUTATIONAL-2", "COMPUTATIONAL-3",
        "COMPUTATIONAL-4", "COMPUTATIONAL-5",
        "PACKED-DECIMAL", "DISPLAY", "POINTER", "INDEX",
        "USAGE", "PROCEDURE-POINTER", "FUNCTION-POINTER",
        "BINARY-CHAR", "BINARY-SHORT", "BINARY-LONG", "BINARY-DOUBLE",
        "FLOAT-SHORT", "FLOAT-LONG", "FLOAT-EXTENDED",
        "NATIONAL", "GROUP-USAGE",
    };

    // Level numbers that act like keywords at the start of a data description.
    private static readonly HashSet<string> LevelNumbers = new()
    {
        "01", "02", "03", "04", "05", "06", "07", "08", "09",
        "10", "11", "12", "13", "14", "15", "16", "17", "18", "19",
        "20", "21", "22", "23", "24", "25", "26", "27", "28", "29",
        "30", "31", "32", "33", "34", "35", "36", "37", "38", "39",
        "40", "41", "42", "43", "44", "45", "46", "47", "48", "49",
        "66", "77", "78", "88",
    };

    protected override LexerState TokenizeNormal(
        string line, ref int pos, List<Token> tokens, LexerState state)
    {
        // ── Fixed-format column 7 comment ──────────────────────────
        // In fixed-format COBOL, column 7 (index 6) indicates a comment
        // line if it contains '*', '/' or 'D'/'d' (debug line).
        if (pos == 0 && line.Length > 6)
        {
            char col7 = line[6];
            if (col7 == '*' || col7 == '/')
            {
                tokens.Add(new Token(0, line.Length, TokenType.Comment));
                pos = line.Length;
                return state;
            }
            if (col7 == 'D' || col7 == 'd')
            {
                tokens.Add(new Token(0, line.Length, TokenType.Comment));
                pos = line.Length;
                return state;
            }
        }

        if (SkipWhitespace(line, ref pos, tokens))
            return state;

        char c = line[pos];

        // ── Inline comment: *> ─────────────────────────────────────
        if (c == '*' && pos + 1 < line.Length && line[pos + 1] == '>')
        {
            ReadLineComment(line, ref pos, tokens);
            return state;
        }

        // ── Strings ────────────────────────────────────────────────
        if (c == '"' || c == '\'')
        {
            ReadCobolString(line, ref pos, tokens);
            return state;
        }

        // ── Numbers ────────────────────────────────────────────────
        if (char.IsDigit(c))
        {
            // Check for level numbers: digits at the start of a statement
            // (first non-space token on the line so far, or after only whitespace).
            int len = 0;
            while (pos + len < line.Length && char.IsDigit(line[pos + len]))
                len++;

            // If followed by a space (not a decimal point), check if it's a level number.
            string numStr = line.Substring(pos, len);
            bool isLevel = LevelNumbers.Contains(numStr) && IsFirstToken(tokens);
            if (isLevel)
            {
                tokens.Add(new Token(pos, len, TokenType.Keyword));
                pos += len;
                return state;
            }

            ReadNumber(line, ref pos, tokens);
            return state;
        }

        if (c == '.' && pos + 1 < line.Length && char.IsDigit(line[pos + 1]))
        {
            ReadNumber(line, ref pos, tokens);
            return state;
        }

        // ── Identifiers and keywords ───────────────────────────────
        if (IsCobolIdentStart(c))
        {
            int start = pos;
            pos++;
            // COBOL identifiers allow letters, digits, and hyphens.
            while (pos < line.Length && IsCobolIdentPart(line[pos]))
                pos++;

            // Trim trailing hyphen (not valid at end of COBOL word).
            while (pos > start + 1 && line[pos - 1] == '-')
                pos--;

            int length = pos - start;
            string word = line.Substring(start, length);
            TokenType type;
            if (Keywords.Contains(word))
                type = TokenType.Keyword;
            else if (TypeNames.Contains(word))
                type = TokenType.TypeName;
            else
                type = TokenType.Identifier;

            tokens.Add(new Token(start, length, type));

            // ── PIC/PICTURE clause: highlight the picture string ───
            if (string.Equals(word, "PIC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(word, "PICTURE", StringComparison.OrdinalIgnoreCase))
            {
                // Skip optional whitespace + optional "IS".
                SkipWhitespace(line, ref pos, tokens);
                if (pos < line.Length)
                {
                    int peek = pos;
                    int idLen = ReadIdentifierLength(line, peek);
                    if (idLen > 0 && string.Equals(
                        line.Substring(peek, idLen), "IS", StringComparison.OrdinalIgnoreCase))
                    {
                        tokens.Add(new Token(peek, idLen, TokenType.Keyword));
                        pos = peek + idLen;
                        SkipWhitespace(line, ref pos, tokens);
                    }
                }

                // Read the picture string (e.g., 9(5)V99, X(20), S9(4)COMP-3).
                if (pos < line.Length && !char.IsWhiteSpace(line[pos]) && line[pos] != '.')
                {
                    int picStart = pos;
                    while (pos < line.Length && !char.IsWhiteSpace(line[pos]) && line[pos] != '.')
                        pos++;
                    tokens.Add(new Token(picStart, pos - picStart, TokenType.String));
                }
            }

            return state;
        }

        // ── Period (statement terminator) ──────────────────────────
        if (c == '.')
        {
            EmitPunctuation(line, ref pos, tokens);
            return state;
        }

        // ── Operators ──────────────────────────────────────────────
        if (c == '=' || c == '<' || c == '>' || c == '+' || c == '-' ||
            c == '*' || c == '/' || c == '&')
        {
            int len = 1;
            if (pos + 1 < line.Length)
            {
                string two = line.Substring(pos, 2);
                if (two is ">=" or "<=" or "**")
                    len = 2;
            }
            EmitOperator(line, ref pos, tokens, len);
            return state;
        }

        // ── Punctuation ────────────────────────────────────────────
        if (c == '(' || c == ')' || c == ',' || c == ';' || c == ':')
        {
            EmitPunctuation(line, ref pos, tokens);
            return state;
        }

        tokens.Add(new Token(pos, 1, TokenType.Plain));
        pos++;
        return state;
    }

    // No multi-line constructs in COBOL — no ContinueMultiLineState override needed.

    /// <summary>
    /// Returns true if <paramref name="c"/> can start a COBOL identifier
    /// (letter or underscore).
    /// </summary>
    private static bool IsCobolIdentStart(char c) =>
        char.IsLetter(c) || c == '_';

    /// <summary>
    /// Returns true if <paramref name="c"/> can continue a COBOL identifier
    /// (letter, digit, or hyphen).
    /// </summary>
    private static bool IsCobolIdentPart(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_';

    /// <summary>
    /// Returns true if the tokens collected so far contain only whitespace
    /// (i.e. this is the first meaningful token on the line).
    /// </summary>
    private static bool IsFirstToken(List<Token> tokens)
    {
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            if (tokens[i].Type != TokenType.Plain)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Reads a COBOL string literal delimited by <c>"</c> or <c>'</c>.
    /// Doubled quotes (<c>""</c> or <c>''</c>) act as escape.
    /// </summary>
    private static void ReadCobolString(string line, ref int pos, List<Token> tokens)
    {
        int start = pos;
        char quote = line[pos];
        pos++;

        while (pos < line.Length)
        {
            if (line[pos] == quote)
            {
                pos++;
                // Doubled quote = escape, continue.
                if (pos < line.Length && line[pos] == quote)
                {
                    pos++;
                    continue;
                }
                break;
            }
            pos++;
        }

        tokens.Add(new Token(start, pos - start, TokenType.String));
    }
}
