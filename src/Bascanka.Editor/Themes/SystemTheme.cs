using Bascanka.Core.Syntax;
using Microsoft.Win32;

namespace Bascanka.Editor.Themes;

public sealed class SystemTheme : ITheme
{
    public string Name => "System";
    DarkTheme dark = new DarkTheme();
    LightTheme light = new LightTheme();
    ITheme theme;
    public SystemTheme() 
    {
        theme = dark;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value is int intValue)
                    {
                        theme= intValue == 1 ? light : dark;
                    }
                }
            }
        }
        catch
        {
        
        }
    }
 
    // ── Syntax highlighting ───────────────────────────────────────────

    public Color GetTokenColor(TokenType type) => theme.GetTokenColor(type);

    // ── Editor surface ────────────────────────────────────────────────

    public Color EditorBackground => theme.EditorBackground; 
    public Color EditorForeground => theme.EditorForeground;

    // ── Gutter ────────────────────────────────────────────────────────

    public Color GutterBackground => theme.GutterBackground;
    public Color GutterForeground => theme.GutterForeground;
    public Color GutterCurrentLine => theme.GutterCurrentLine;

    // ── Current line / selection ──────────────────────────────────────

    public Color LineHighlight => theme.LineHighlight;
    public Color SelectionBackground => theme.SelectionBackground;
    public Color SelectionForeground => theme.SelectionForeground;

    // ── Caret ─────────────────────────────────────────────────────────

    public Color CaretColor => theme.CaretColor;

    // ── Tab bar ───────────────────────────────────────────────────────

    public Color TabBarBackground => theme.TabBarBackground;
    public Color TabActiveBackground => theme.TabActiveBackground;
    public Color TabInactiveBackground => theme.TabInactiveBackground;
    public Color TabActiveForeground => theme.TabActiveForeground;
    public Color TabInactiveForeground => theme.TabInactiveForeground;
    public Color TabBorder => theme.TabBorder;

    // ── Status bar ────────────────────────────────────────────────────

    public Color StatusBarBackground => theme.StatusBarBackground;
    public Color StatusBarForeground => theme.StatusBarForeground;

    // ── Find / replace panel ──────────────────────────────────────────

    public Color FindPanelBackground =>  theme.FindPanelBackground;
    public Color FindPanelForeground => theme.FindPanelForeground;
    public Color MatchHighlight => theme.MatchHighlight;

    // ── Bracket matching ──────────────────────────────────────────────

    public Color BracketMatchBackground => theme.BracketMatchBackground;

    // ── Context menus ─────────────────────────────────────────────────

    public Color MenuBackground =>  theme.MenuBackground; 
    public Color MenuForeground => theme.MenuForeground;
    public Color MenuHighlight => theme.MenuHighlight;

    // ── Scroll bar ────────────────────────────────────────────────────

    public Color ScrollBarBackground =>  theme.ScrollBarBackground;
    public Color ScrollBarThumb =>  theme.ScrollBarThumb;

    // ── Diff highlighting ────────────────────────────────────────────
    public Color DiffAddedBackground =>  theme.DiffAddedBackground;
    public Color DiffRemovedBackground => theme.DiffRemovedBackground;
    public Color DiffModifiedBackground =>  theme.DiffModifiedBackground;
    public Color DiffModifiedCharBackground => theme.DiffModifiedCharBackground;
    public Color DiffPaddingBackground => theme.DiffPaddingBackground;
    public Color DiffGutterMarker =>    theme.DiffGutterMarker;

    // ── Miscellaneous ─────────────────────────────────────────────────

    public Color FoldingMarker => theme.FoldingMarker;
    public Color ModifiedIndicator => theme.ModifiedIndicator;
}