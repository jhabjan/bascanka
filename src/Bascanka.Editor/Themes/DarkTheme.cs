using System.Drawing;
using Bascanka.Core.Syntax;

namespace Bascanka.Editor.Themes;

/// <summary>
/// A dark colour theme inspired by the Visual Studio Code "Dark+" default theme.
/// </summary>
public sealed class DarkTheme : ITheme
{
    public string Name => "Dark";

    // ── Syntax highlighting ───────────────────────────────────────────

    public Color GetTokenColor(TokenType type) => type switch
    {
        TokenType.Keyword           => ColorTranslator.FromHtml("#569CD6"),
        TokenType.TypeName          => ColorTranslator.FromHtml("#4EC9B0"),
        TokenType.Identifier        => ColorTranslator.FromHtml("#9CDCFE"),
        TokenType.String            => ColorTranslator.FromHtml("#CE9178"),
        TokenType.Character         => ColorTranslator.FromHtml("#CE9178"),
        TokenType.Number            => ColorTranslator.FromHtml("#B5CEA8"),
        TokenType.Comment           => ColorTranslator.FromHtml("#6A9955"),
        TokenType.MultiLineComment  => ColorTranslator.FromHtml("#6A9955"),
        TokenType.Operator          => ColorTranslator.FromHtml("#D4D4D4"),
        TokenType.Punctuation       => ColorTranslator.FromHtml("#D4D4D4"),
        TokenType.Preprocessor      => ColorTranslator.FromHtml("#C586C0"),
        TokenType.Attribute         => ColorTranslator.FromHtml("#4EC9B0"),
        TokenType.Regex             => ColorTranslator.FromHtml("#D16969"),
        TokenType.Escape            => ColorTranslator.FromHtml("#D7BA7D"),
        TokenType.Tag               => ColorTranslator.FromHtml("#569CD6"),
        TokenType.TagAttribute      => ColorTranslator.FromHtml("#9CDCFE"),
        TokenType.TagAttributeValue => ColorTranslator.FromHtml("#CE9178"),
        TokenType.Entity            => ColorTranslator.FromHtml("#569CD6"),
        TokenType.MarkdownHeading   => ColorTranslator.FromHtml("#569CD6"),
        TokenType.MarkdownBold      => ColorTranslator.FromHtml("#569CD6"),
        TokenType.MarkdownItalic    => ColorTranslator.FromHtml("#569CD6"),
        TokenType.MarkdownCode      => ColorTranslator.FromHtml("#CE9178"),
        TokenType.MarkdownLink      => ColorTranslator.FromHtml("#4EC9B0"),
        TokenType.JsonKey           => ColorTranslator.FromHtml("#9CDCFE"),
        TokenType.JsonString        => ColorTranslator.FromHtml("#FF8AD8"),
        _                           => EditorForeground,
    };

    // ── Editor surface ────────────────────────────────────────────────

    public Color EditorBackground => ColorTranslator.FromHtml("#1E1E1E");
    public Color EditorForeground => ColorTranslator.FromHtml("#D4D4D4");

    // ── Gutter ────────────────────────────────────────────────────────

    public Color GutterBackground => ColorTranslator.FromHtml("#1E1E1E");
    public Color GutterForeground => ColorTranslator.FromHtml("#858585");
    public Color GutterCurrentLine => ColorTranslator.FromHtml("#C6C6C6");

    // ── Current line / selection ──────────────────────────────────────

    public Color LineHighlight        => ColorTranslator.FromHtml("#2A2D2E");
    public Color SelectionBackground  => ColorTranslator.FromHtml("#264F78");
    public Color SelectionForeground  => ColorTranslator.FromHtml("#D4D4D4");

    // ── Caret ─────────────────────────────────────────────────────────

    public Color CaretColor => ColorTranslator.FromHtml("#AEAFAD");

    // ── Tab bar ───────────────────────────────────────────────────────

    public Color TabBarBackground     => ColorTranslator.FromHtml("#252526");
    public Color TabActiveBackground  => ColorTranslator.FromHtml("#2A2A2A");
    public Color TabInactiveBackground => ColorTranslator.FromHtml("#2D2D2D");
    public Color TabActiveForeground  => ColorTranslator.FromHtml("#FFFFFF");
    public Color TabInactiveForeground => ColorTranslator.FromHtml("#969696");
    public Color TabBorder            => ColorTranslator.FromHtml("#252526");

    // ── Status bar ────────────────────────────────────────────────────

    public Color StatusBarBackground => ColorTranslator.FromHtml("#007ACC");
    public Color StatusBarForeground => ColorTranslator.FromHtml("#FFFFFF");

    // ── Find / replace panel ──────────────────────────────────────────

    public Color FindPanelBackground => ColorTranslator.FromHtml("#333337");
    public Color FindPanelForeground => ColorTranslator.FromHtml("#D4D4D4");
    public Color MatchHighlight      => Color.FromArgb(100, 234, 92, 0);

    // ── Bracket matching ──────────────────────────────────────────────

    public Color BracketMatchBackground => Color.FromArgb(80, 97, 175, 239);

    // ── Context menus ─────────────────────────────────────────────────

    public Color MenuBackground => ColorTranslator.FromHtml("#252526");
    public Color MenuForeground => ColorTranslator.FromHtml("#CCCCCC");
    public Color MenuHighlight  => ColorTranslator.FromHtml("#094771");

    // ── Scroll bar ────────────────────────────────────────────────────

    public Color ScrollBarBackground => ColorTranslator.FromHtml("#1E1E1E");
    public Color ScrollBarThumb      => Color.FromArgb(100, 121, 121, 121);

    // ── Diff highlighting ────────────────────────────────────────────
    public Color DiffAddedBackground       => Color.FromArgb(55, 50, 200, 50);
    public Color DiffRemovedBackground     => Color.FromArgb(55, 160, 50, 220);
    public Color DiffModifiedBackground    => Color.FromArgb(50, 50, 130, 255);
    public Color DiffModifiedCharBackground => Color.FromArgb(120, 70, 160, 255);
    public Color DiffPaddingBackground     => Color.FromArgb(20, 160, 160, 180);
    public Color DiffGutterMarker          => Color.FromArgb(200, 80, 160, 255);

    // ── Miscellaneous ─────────────────────────────────────────────────

    public Color FoldingMarker     => ColorTranslator.FromHtml("#C5C5C5");
    public Color ModifiedIndicator => ColorTranslator.FromHtml("#E2C08D");
}
