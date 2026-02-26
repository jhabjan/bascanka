using System.Drawing.Drawing2D;
using static Enums;

namespace Bascanka.Editor.Panels;


/// <summary>
/// A lightweight owner-drawn button with rounded corners, hover effect,
/// and optional toggle state. Used for all buttons in the find/replace panel.
/// </summary>
internal sealed class PanelButton : Control
{
	private bool _hovered;
	private bool _pressed;
	private bool _isActive;

	public PanelButtonMode ButtonMode { get; set; } = PanelButtonMode.Icon;

	public Color NormalBg { get; set; } = Color.Transparent;
	public Color HoverBg { get; set; } = Color.FromArgb(50, 128, 128, 128);
	public Color BorderColor { get; set; } = Color.FromArgb(60, 128, 128, 128);

	// Toggle-specific colours.
	public Color ActiveBg { get; set; } = Color.FromArgb(40, 80, 140);
	public Color ActiveBorder { get; set; } = Color.FromArgb(70, 120, 190);
	public Color ActiveFg { get; set; } = Color.White;

	public bool IsActive
	{
		get => _isActive;
		set { _isActive = value; Invalidate(); }
	}

	public PanelButton()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
				 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
				 ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
		BackColor = Color.Transparent;
	}

	public override Size GetPreferredSize(Size proposedSize)
	{
		// Compute size based on text + padding so AutoSize works.
		var textSize = TextRenderer.MeasureText(Text, Font);
		int hPad = ButtonMode == PanelButtonMode.Text ? Padding.Horizontal + 16 : 8;
		int vPad = 4;
		return new Size(textSize.Width + hPad, Math.Max(Height, textSize.Height + vPad));
	}

	protected override void OnTextChanged(EventArgs e)
	{
		base.OnTextChanged(e);
		if (AutoSize)
		{
			var pref = GetPreferredSize(Size.Empty);
			Width = pref.Width;
		}
		Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;

		var rect = new Rectangle(0, 0, Width - 1, Height - 1);
		int radius = ButtonMode == PanelButtonMode.Icon ? 3 : 4;

		// Determine background colour.
		Color bg;
		Color fg;
		bool showBorder;

		if (ButtonMode == PanelButtonMode.Toggle && _isActive)
		{
			bg = _pressed ? Darken(ActiveBg, 10)
			   : _hovered ? Lighten(ActiveBg, 10)
			   : ActiveBg;
			fg = ActiveFg;
			showBorder = true;
		}
		else
		{
			bg = !Enabled ? NormalBg
			   : _pressed ? Color.FromArgb(Math.Min(HoverBg.A + 40, 255), HoverBg.R, HoverBg.G, HoverBg.B)
			   : _hovered ? HoverBg
			   : NormalBg;
			fg = Enabled ? ForeColor : Color.FromArgb(100, ForeColor);
			showBorder = (_hovered || _pressed) && ButtonMode != PanelButtonMode.Icon;
		}

		// Draw background.
		using var path = CreateRoundedRect(rect, radius);
		using var brush = new SolidBrush(bg);
		g.FillPath(brush, path);

		// Draw border.
		if (showBorder || (ButtonMode == PanelButtonMode.Toggle && _isActive))
		{
			Color borderCol = (_isActive && ButtonMode == PanelButtonMode.Toggle)
				? ActiveBorder
				: BorderColor;
			using var pen = new Pen(borderCol);
			g.DrawPath(pen, path);
		}

		// For text mode, show a subtle border always (even when not hovered).
		if (ButtonMode == PanelButtonMode.Text && !_hovered && !_pressed)
		{
			using var subtlePen = new Pen(Color.FromArgb(30, BorderColor.R, BorderColor.G, BorderColor.B));
			g.DrawPath(subtlePen, path);
		}

		// For icon buttons, show a subtle hover-only rounded bg.
		if (ButtonMode == PanelButtonMode.Icon && _hovered && !_pressed)
		{
			using var hoverPen = new Pen(Color.FromArgb(40, BorderColor.R, BorderColor.G, BorderColor.B));
			g.DrawPath(hoverPen, path);
		}

		// Active toggle indicator — coloured bottom bar.
		if (ButtonMode == PanelButtonMode.Toggle && _isActive)
		{
			int barY = Height - 3;
			int barInset = 4;
			using var barBrush = new SolidBrush(ActiveBorder);
			g.FillRectangle(barBrush, barInset, barY, Width - barInset * 2, 2);
		}

		// Draw text.
		TextRenderer.DrawText(g, Text, Font, rect, fg,
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

	private static Color Lighten(Color c, int amount)
	{
		return Color.FromArgb(c.A,
			Math.Min(255, c.R + amount),
			Math.Min(255, c.G + amount),
			Math.Min(255, c.B + amount));
	}

	private static Color Darken(Color c, int amount)
	{
		return Color.FromArgb(c.A,
			Math.Max(0, c.R - amount),
			Math.Max(0, c.G - amount),
			Math.Max(0, c.B - amount));
	}
}

