using Bascanka.Editor.Themes;

namespace Bascanka.App;


/// <summary>
/// Custom colour table that overrides the professional colour scheme
/// with theme-aware colours.
/// </summary>
internal sealed class ThemedColorTable(ITheme theme) : ProfessionalColorTable
{
	private readonly ITheme _theme = theme;

	public override Color MenuStripGradientBegin => _theme.MenuBackground;
	public override Color MenuStripGradientEnd => _theme.MenuBackground;
	public override Color MenuItemSelected => _theme.MenuHighlight;
	public override Color MenuItemSelectedGradientBegin => _theme.MenuHighlight;
	public override Color MenuItemSelectedGradientEnd => _theme.MenuHighlight;
	public override Color MenuItemPressedGradientBegin => _theme.MenuHighlight;
	public override Color MenuItemPressedGradientEnd => _theme.MenuHighlight;
	public override Color MenuBorder => Lighten(_theme.MenuBackground, 40);
	public override Color MenuItemBorder => _theme.MenuHighlight;
	public override Color ImageMarginGradientBegin => _theme.MenuBackground;
	public override Color ImageMarginGradientMiddle => _theme.MenuBackground;
	public override Color ImageMarginGradientEnd => _theme.MenuBackground;
	public override Color SeparatorDark => ColorHelper.SeparatorColor(_theme.MenuBackground);
	public override Color SeparatorLight => ColorHelper.SeparatorColor(_theme.MenuBackground);
	public override Color ToolStripDropDownBackground => _theme.MenuBackground;
	public override Color ToolStripContentPanelGradientBegin => _theme.MenuBackground;
	public override Color ToolStripContentPanelGradientEnd => _theme.MenuBackground;
	public override Color CheckBackground => _theme.MenuHighlight;
	public override Color CheckSelectedBackground => _theme.MenuHighlight;
	public override Color CheckPressedBackground => _theme.MenuHighlight;

	private static Color Lighten(Color c, int amount) =>
		Color.FromArgb(c.A,
			Math.Min(255, c.R + amount),
			Math.Min(255, c.G + amount),
			Math.Min(255, c.B + amount));
}

