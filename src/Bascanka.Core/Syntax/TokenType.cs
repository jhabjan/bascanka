namespace Bascanka.Core.Syntax;

/// <summary>
/// Identifies the syntactic role of a token produced by a lexer.
/// Used by the rendering layer to determine how each span of text should be coloured.
/// </summary>
public enum TokenType : byte
{
    /// <summary>Plain text with no special highlighting.</summary>
    Plain = 0,

    /// <summary>A reserved language keyword (e.g. <c>if</c>, <c>class</c>, <c>return</c>).</summary>
    Keyword,

    /// <summary>A type name such as a class, struct, enum, or interface name.</summary>
    TypeName,

    /// <summary>A general identifier (variable, function, field, etc.).</summary>
    Identifier,

    /// <summary>A string literal, including the enclosing quotes.</summary>
    String,

    /// <summary>A character literal (e.g. <c>'a'</c>).</summary>
    Character,

    /// <summary>A numeric literal (integer, floating-point, hex, binary, etc.).</summary>
    Number,

    /// <summary>A single-line comment (e.g. <c>// ...</c> or <c># ...</c>).</summary>
    Comment,

    /// <summary>A multi-line (block) comment (e.g. <c>/* ... */</c>).</summary>
    MultiLineComment,

    /// <summary>An operator symbol (e.g. <c>+</c>, <c>==</c>, <c>=&gt;</c>).</summary>
    Operator,

    /// <summary>Punctuation such as braces, parentheses, semicolons, commas.</summary>
    Punctuation,

    /// <summary>A preprocessor directive (e.g. <c>#if</c>, <c>#include</c>).</summary>
    Preprocessor,

    /// <summary>An attribute or annotation (e.g. <c>[Serializable]</c>, <c>@Override</c>).</summary>
    Attribute,

    /// <summary>A regular-expression literal.</summary>
    Regex,

    /// <summary>An escape sequence inside a string or character literal.</summary>
    Escape,

    /// <summary>A markup tag name (e.g. <c>&lt;div&gt;</c>).</summary>
    Tag,

    /// <summary>An attribute name inside a markup tag.</summary>
    TagAttribute,

    /// <summary>An attribute value inside a markup tag.</summary>
    TagAttributeValue,

    /// <summary>An HTML/XML character entity (e.g. <c>&amp;amp;</c>).</summary>
    Entity,

    /// <summary>A Markdown heading (lines starting with <c>#</c>).</summary>
    MarkdownHeading,

    /// <summary>Bold text in Markdown (<c>**text**</c>).</summary>
    MarkdownBold,

    /// <summary>Italic text in Markdown (<c>*text*</c>).</summary>
    MarkdownItalic,

    /// <summary>Inline or fenced code in Markdown.</summary>
    MarkdownCode,

    /// <summary>A link in Markdown (<c>[text](url)</c>).</summary>
    MarkdownLink,

    /// <summary>A JSON object key (property name).</summary>
    JsonKey,

    /// <summary>A JSON string value.</summary>
    JsonString,
}
