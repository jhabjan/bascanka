namespace Bascanka.App
{
	internal static class ColorHelper
	{
		/// <summary>
		/// Returns a separator colour that contrasts with the background —
		/// darkens on light backgrounds, lightens on dark backgrounds.
		/// </summary>
		public static Color SeparatorColor(Color bg)
		{
			float lum = (bg.R * 0.299f + bg.G * 0.587f + bg.B * 0.114f) / 255f;
			int amount = 35;
			if (lum > 0.5f)
				// Light theme: darken
				return Color.FromArgb(bg.A,
					Math.Max(0, bg.R - amount),
					Math.Max(0, bg.G - amount),
					Math.Max(0, bg.B - amount));
			else
				// Dark theme: lighten
				return Color.FromArgb(bg.A,
					Math.Min(255, bg.R + amount),
					Math.Min(255, bg.G + amount),
					Math.Min(255, bg.B + amount));
		}

		public static Color Lighten(Color c, int amount) =>
	   Color.FromArgb(c.A,
		   Math.Min(255, c.R + amount),
		   Math.Min(255, c.G + amount),
		   Math.Min(255, c.B + amount));

	}
}
