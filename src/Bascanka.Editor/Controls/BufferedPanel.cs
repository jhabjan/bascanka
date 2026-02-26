namespace Bascanka.Editor.Controls;


/// <summary>
/// A <see cref="Panel"/> subclass with double-buffering enabled to
/// eliminate flicker during rapid repainting (e.g. scrolling).
/// </summary>
internal sealed class BufferedPanel : Panel
{
	public BufferedPanel()
	{
		SetStyle(
			ControlStyles.AllPaintingInWmPaint |
			ControlStyles.UserPaint |
			ControlStyles.OptimizedDoubleBuffer,
			true);
	}
}

