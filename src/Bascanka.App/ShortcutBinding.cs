namespace Bascanka.App;

internal sealed class ShortcutBinding
{
	public string CommandName { get; set; } = string.Empty;
	public Keys Key { get; set; }
	public bool Ctrl { get; set; }
	public bool Shift { get; set; }
	public bool Alt { get; set; }
	public Action? Handler { get; set; }
}

