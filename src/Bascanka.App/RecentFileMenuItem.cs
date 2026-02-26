namespace Bascanka.App;

/// <summary>
/// Menu item for recent files. Stores the split path so the themed
/// renderer can draw directory and filename in different colors.
/// </summary>
internal sealed class RecentFileMenuItem : ToolStripMenuItem
{
	public readonly string DisplayName;
	public readonly string DisplayDir;
	public int NameColumnWidth;

	private const int MaxChars = 100;

	public RecentFileMenuItem(string fullPath) : base(fullPath)
	{
		string fileName = Path.GetFileName(fullPath);
		string dirPart = fullPath.Length > fileName.Length
			? fullPath[..^fileName.Length]
			: string.Empty;
		DisplayName = TruncateMiddle(fileName, MaxChars);
		DisplayDir = TruncateMiddle(dirPart, MaxChars);
	}

	private static string TruncateMiddle(string text, int maxChars)
	{
		if (text.Length <= maxChars)
			return text;

		int half = (maxChars - 1) / 2; // -1 for the ellipsis character
		return string.Concat(text.AsSpan(0, half), "\u2026", text.AsSpan(text.Length - half));
	}
}
