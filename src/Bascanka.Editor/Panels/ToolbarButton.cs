using System.Drawing.Drawing2D;

namespace Bascanka.Editor.Panels;


// ── Custom owner-drawn button ────────────────────────────────────

/// <summary>
/// A lightweight owner-drawn button with rounded corners and hover effect.
/// </summary>
internal sealed class ToolbarButton : Control
{
	private bool _hovered;
	private bool _pressed;

	public Color NormalBg { get; set; } = Color.Transparent;
	public Color HoverBg { get; set; } = Color.FromArgb(50, 128, 128, 128);
	public Color BorderColor { get; set; } = Color.FromArgb(60, 128, 128, 128);

	public ToolbarButton()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
				 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
		BackColor = Color.Transparent;
		Height = 26;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;

		var rect = new Rectangle(0, 0, Width - 1, Height - 1);
		int radius = 4;

		Color bg = !Enabled ? NormalBg
				 : _pressed ? Color.FromArgb(Math.Min(HoverBg.A + 40, 255), HoverBg.R, HoverBg.G, HoverBg.B)
				 : _hovered ? HoverBg
				 : NormalBg;

		using var path = CreateRoundedRect(rect, radius);
		using var brush = new SolidBrush(bg);
		g.FillPath(brush, path);

		if (_hovered || _pressed)
		{
			using var pen = new Pen(BorderColor);
			g.DrawPath(pen, path);
		}

		var textColor = Enabled ? ForeColor : Color.FromArgb(100, ForeColor);
		TextRenderer.DrawText(g, Text, Font, rect, textColor,
			TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
			TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		_hovered = true;
		Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hovered = false;
		_pressed = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		_pressed = true;
		Invalidate();
		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		_pressed = false;
		Invalidate();
		base.OnMouseUp(e);
	}

	private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
	{
		var path = new GraphicsPath();
		int d = radius * 2;
		path.AddArc(rect.X, rect.Y, d, d, 180, 90);
		path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
		path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
		path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
		path.CloseFigure();
		return path;
	}
}

