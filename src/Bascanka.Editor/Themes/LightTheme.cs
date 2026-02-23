using System.Drawing;
using Bascanka.Core.Syntax;

namespace Bascanka.Editor.Themes;

/// <summary>
/// A light colour theme inspired by the Visual Studio Code "Light+" default theme.
/// </summary>
public sealed class LightTheme : ITheme
{
    public string Name => "Light";

    // ── Syntax highlighting ───────────────────────────────────────────

    public Color GetTokenColor(TokenType type) => type switch
    {
        TokenType.Keyword           => ColorTranslator.FromHtml("#0000FF"),
        TokenType.TypeName          => ColorTranslator.FromHtml("#267F99"),
        TokenType.Identifier        => ColorTranslator.FromHtml("#001080"),
        TokenType.String            => ColorTranslator.FromHtml("#A31515"),
        TokenType.Character         => ColorTranslator.FromHtml("#A31515"),
        TokenType.Number            => ColorTranslator.FromHtml("#098658"),
        TokenType.Comment           => ColorTranslator.FromHtml("#008000"),
        TokenType.MultiLineComment  => ColorTranslator.FromHtml("#008000"),
        TokenType.Operator          => ColorTranslator.FromHtml("#000000"),
        TokenType.Punctuation       => ColorTranslator.FromHtml("#000000"),
        TokenType.Preprocessor      => ColorTranslator.FromHtml("#AF00DB"),
        TokenType.Attribute         => ColorTranslator.FromHtml("#267F99"),
        TokenType.Regex             => ColorTranslator.FromHtml("#811F3F"),
        TokenType.Escape            => ColorTranslator.FromHtml("#EE0000"),
        TokenType.Tag               => ColorTranslator.FromHtml("#800000"),
        TokenType.TagAttribute      => ColorTranslator.FromHtml("#FF0000"),
        TokenType.TagAttributeValue => ColorTranslator.FromHtml("#0000FF"),
        TokenType.Entity            => ColorTranslator.FromHtml("#800000"),
        TokenType.MarkdownHeading   => ColorTranslator.FromHtml("#0000FF"),
        TokenType.MarkdownBold      => ColorTranslator.FromHtml("#000000"),
        TokenType.MarkdownItalic    => ColorTranslator.FromHtml("#000000"),
        TokenType.MarkdownCode      => ColorTranslator.FromHtml("#A31515"),
        TokenType.MarkdownLink      => ColorTranslator.FromHtml("#267F99"),
        TokenType.JsonKey           => ColorTranslator.FromHtml("#0451A5"),
        TokenType.JsonString        => ColorTranslator.FromHtml("#C2185B"),
        _                           => EditorForeground,
    };

    // ── Editor surface ────────────────────────────────────────────────

    public Color EditorBackground => ColorTranslator.FromHtml("#FFFFFF");
    public Color EditorForeground => ColorTranslator.FromHtml("#000000");

    // ── Gutter ────────────────────────────────────────────────────────

    public Color GutterBackground => ColorTranslator.FromHtml("#FFFFFF");
    public Color GutterForeground => ColorTranslator.FromHtml("#237893");
    public Color GutterCurrentLine => ColorTranslator.FromHtml("#0B216F");

    // ── Current line / selection ──────────────────────────────────────

    public Color LineHighlight        => ColorTranslator.FromHtml("#F3F3F3");
    public Color SelectionBackground  => ColorTranslator.FromHtml("#ADD6FF");
    public Color SelectionForeground  => ColorTranslator.FromHtml("#000000");

    // ── Caret ─────────────────────────────────────────────────────────

    public Color CaretColor => ColorTranslator.FromHtml("#000000");

    // ── Tab bar ───────────────────────────────────────────────────────

    public Color TabBarBackground     => ColorTranslator.FromHtml("#F3F3F3");
    public Color TabActiveBackground  => ColorTranslator.FromHtml("#F0F0F0");
    public Color TabInactiveBackground => ColorTranslator.FromHtml("#ECECEC");
    public Color TabActiveForeground  => ColorTranslator.FromHtml("#333333");
    public Color TabInactiveForeground => ColorTranslator.FromHtml("#999999");
    public Color TabBorder            => ColorTranslator.FromHtml("#F3F3F3");

    // ── Status bar ────────────────────────────────────────────────────

    public Color StatusBarBackground => ColorTranslator.FromHtml("#007ACC");
    public Color StatusBarForeground => ColorTranslator.FromHtml("#FFFFFF");

    // ── Find / replace panel ──────────────────────────────────────────

    public Color FindPanelBackground => ColorTranslator.FromHtml("#E8E8EC");
    public Color FindPanelForeground => ColorTranslator.FromHtml("#1E1E1E");
    public Color MatchHighlight      => Color.FromArgb(80, 234, 92, 0);

    // ── Bracket matching ──────────────────────────────────────────────

    public Color BracketMatchBackground => Color.FromArgb(60, 0, 100, 200);

    // ── Context menus ─────────────────────────────────────────────────

    public Color MenuBackground => ColorTranslator.FromHtml("#F3F3F3");
    public Color MenuForeground => ColorTranslator.FromHtml("#000000");
    public Color MenuHighlight  => ColorTranslator.FromHtml("#C4DCF0");

    // ── Scroll bar ────────────────────────────────────────────────────

    public Color ScrollBarBackground => ColorTranslator.FromHtml("#F3F3F3");
    public Color ScrollBarThumb      => Color.FromArgb(100, 100, 100, 100);

    // ── Diff highlighting ────────────────────────────────────────────
    public Color DiffAddedBackground       => Color.FromArgb(90, 0, 180, 220);
    public Color DiffRemovedBackground     => Color.FromArgb(90, 220, 50, 160);
    public Color DiffModifiedBackground    => Color.FromArgb(80, 140, 80, 230);
    public Color DiffModifiedCharBackground => Color.FromArgb(120, 160, 100, 245);
    public Color DiffPaddingBackground     => Color.FromArgb(35, 140, 140, 160);
    public Color DiffGutterMarker          => Color.FromArgb(220, 140, 60, 220);

    // ── Miscellaneous ─────────────────────────────────────────────────

    public Color FoldingMarker     => ColorTranslator.FromHtml("#424242");
    public Color ModifiedIndicator => ColorTranslator.FromHtml("#C18401");
}
