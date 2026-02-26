using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Controls;



// ────────────────────────────────────────────────────────────────────
//  Double-buffered panel (used for the gutter)
// ────────────────────────────────────────────────────────────────────

/// <summary>
/// Custom renderer for the editor context menu that uses theme colours
/// for background, hover highlight, text, and separators.
/// </summary>
internal sealed class ThemedContextMenuRenderer(ITheme theme) : ToolStripProfessionalRenderer
{
	private readonly ITheme _theme = theme;

	protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
	{
		using var brush = new SolidBrush(_theme.MenuBackground);
		e.Graphics.FillRectangle(brush, e.AffectedBounds);
	}

	protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
	{
		var rect = new Rectangle(Point.Empty, e.Item.Size);
		Color bg = e.Item.Selected || e.Item.Pressed ? _theme.MenuHighlight : _theme.MenuBackground;
		using var brush = new SolidBrush(bg);
		e.Graphics.FillRectangle(brush, rect);
	}

	protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
	{
		e.TextColor = _theme.MenuForeground;
		base.OnRenderItemText(e);
	}

	protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
	{
		int y = e.Item.Height / 2;
		Color sep = Color.FromArgb(
			_theme.MenuBackground.A,
			Math.Min(255, _theme.MenuBackground.R + 30),
			Math.Min(255, _theme.MenuBackground.G + 30),
			Math.Min(255, _theme.MenuBackground.B + 30));
		using var pen = new Pen(sep);
		e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
	}

	protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
	{
		using var brush = new SolidBrush(_theme.MenuBackground);
		e.Graphics.FillRectangle(brush, e.AffectedBounds);
	}

	protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
	{
		Color border = Color.FromArgb(
			_theme.MenuBackground.A,
			Math.Min(255, _theme.MenuBackground.R + 40),
			Math.Min(255, _theme.MenuBackground.G + 40),
			Math.Min(255, _theme.MenuBackground.B + 40));
		using var pen = new Pen(border);
		e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
	}
}

