using System.Drawing.Drawing2D;
using Bascanka.Editor.Themes;

namespace Bascanka.App;

/// <summary>
/// Custom <see cref="ToolStripProfessionalRenderer"/> that draws menus
/// and context menus using colours from the active <see cref="ITheme"/>.
/// </summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public const int DefaultMenuItemPadding = 5;
    public static int ConfigMenuItemPadding { get; set; } = DefaultMenuItemPadding;

    private readonly ITheme _theme;

    public ThemedMenuRenderer(ITheme theme)
        : base(new ThemedColorTable(theme))
    {
        _theme = theme;
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Vertically center the text rectangle within the item.
        var tr = e.TextRectangle;
        int centeredY = (e.Item.Height - tr.Height) / 2;
        if (centeredY != tr.Y)
        {
            tr = new Rectangle(tr.X, centeredY, tr.Width, tr.Height);
            e.TextRectangle = tr;
        }

        if (e.Item is RecentFileMenuItem rf && rf.NameColumnWidth > 0
            && e.Text == e.Item.Text) // only for the main text pass, not shortcut
        {
            Color dirColor = AccentColor(_theme.MenuForeground);
            Color nameColor = _theme.MenuForeground;

            var flags = e.TextFormat | TextFormatFlags.NoPrefix;
            var g = e.Graphics;
            var font = e.TextFont ?? e.Item.Font;

            const int gap = 16;

            // Filename column (left-aligned, fixed width).
            var nameRect = new Rectangle(tr.X, tr.Y, rf.NameColumnWidth, tr.Height);
            TextRenderer.DrawText(g, rf.DisplayName, font, nameRect, nameColor, flags);

            // Directory column (left-aligned after filename column, smaller italic, dimmer).
            int dirX = tr.X + rf.NameColumnWidth + gap;
            var dirRect = new Rectangle(dirX, tr.Y, tr.Width - rf.NameColumnWidth - gap, tr.Height);
            using var dirFont = new Font(font.FontFamily, font.Size * 0.9f, FontStyle.Italic, font.Unit);
            TextRenderer.DrawText(g, rf.DisplayDir, dirFont, dirRect, dirColor, flags);
            return;
        }

        e.TextColor = _theme.MenuForeground;
        base.OnRenderItemText(e);
    }

    private static Color AccentColor(Color c)
    {
        float lum = (c.R * 0.299f + c.G * 0.587f + c.B * 0.114f) / 255f;
        if (lum > 0.5f)
            return Color.FromArgb(c.A,
                Math.Min(255, c.R + (int)((255 - c.R) * 0.55f)),
                Math.Min(255, c.G + (int)((255 - c.G) * 0.55f)),
                Math.Min(255, c.B + (int)((255 - c.B) * 0.55f)));
        else
            return Color.FromArgb(c.A, (int)(c.R * 0.5f), (int)(c.G * 0.5f), (int)(c.B * 0.5f));
    }

    protected override void Initialize(ToolStrip toolStrip)
    {
        base.Initialize(toolStrip);
        if (toolStrip is ToolStripDropDownMenu ddm)
        {
            foreach (ToolStripItem item in ddm.Items)
                ApplyPadding(item);
        }
    }

    protected override void InitializeItem(ToolStripItem item)
    {
        base.InitializeItem(item);
        ApplyPadding(item);
    }

    private static void ApplyPadding(ToolStripItem item)
    {
        if (item is ToolStripSeparator) return;
        int pad = ConfigMenuItemPadding;
        var p = item.Padding;
        if (p.Top != pad || p.Bottom != pad)
            item.Padding = new Padding(p.Left, pad, p.Right, pad);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        ApplyPadding(e.Item);
        var rect = new Rectangle(Point.Empty, e.Item.Size);

        // Always fill background first.
        using (var bgBrush = new SolidBrush(_theme.MenuBackground))
            e.Graphics.FillRectangle(bgBrush, rect);

        if (e.Item.Selected || e.Item.Pressed)
        {
            const int radius = 6;
            const int inset = 3;
            var hlRect = new Rectangle(
                rect.X + inset, rect.Y + 1,
                rect.Width - inset * 2, rect.Height - 2);

            var oldSmooth = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var path = RoundedRect(hlRect, radius);
            using var brush = new SolidBrush(_theme.MenuHighlight);
            e.Graphics.FillPath(brush, path);

            e.Graphics.SmoothingMode = oldSmooth;
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_theme.MenuBackground);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Draw a subtle border around dropdown menus.
        if (e.ToolStrip is ToolStripDropDownMenu)
        {
            Color borderColor = ColorHelper.Lighten(_theme.MenuBackground, 40);
            using var pen = new Pen(borderColor);
            var rect = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, 0, 0, rect.Width - 1, rect.Height - 1);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        Color sepColor = ColorHelper.SeparatorColor(_theme.MenuBackground);
        int y = e.Item.Height / 2;
        using var pen = new Pen(sepColor);
        e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _theme.MenuForeground;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Fill image margin with menu background to avoid white strip.
        using var brush = new SolidBrush(_theme.MenuBackground);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // Draw checkmark with theme colours.
        var rect = e.ImageRectangle;
        using var brush = new SolidBrush(_theme.MenuHighlight);
        e.Graphics.FillRectangle(brush, rect);
        base.OnRenderItemCheck(e);
    }

   
   
}
