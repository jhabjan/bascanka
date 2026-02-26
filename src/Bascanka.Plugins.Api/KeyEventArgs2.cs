namespace Bascanka.Plugins.Api;

/// <summary>
/// Event arguments for keyboard input events.
/// Named <c>KeyEventArgs2</c> to avoid conflicts with
/// <c>System.Windows.Forms.KeyEventArgs</c>.
/// </summary>
/// <remarks>
/// Initializes a new instance of <see cref="KeyEventArgs2"/>.
/// </remarks>
/// <param name="keyCode">The virtual key code of the pressed key.</param>
/// <param name="control">Whether the Ctrl modifier was held.</param>
/// <param name="shift">Whether the Shift modifier was held.</param>
/// <param name="alt">Whether the Alt modifier was held.</param>
public class KeyEventArgs2(int keyCode, bool control, bool shift, bool alt) : EventArgs
{

	/// <summary>Gets the virtual key code of the pressed key.</summary>
	public int KeyCode { get; } = keyCode;

	/// <summary>Gets a value indicating whether the Ctrl modifier was held.</summary>
	public bool Control { get; } = control;

	/// <summary>Gets a value indicating whether the Shift modifier was held.</summary>
	public bool Shift { get; } = shift;

	/// <summary>Gets a value indicating whether the Alt modifier was held.</summary>
	public bool Alt { get; } = alt;

	/// <summary>
	/// Gets or sets a value indicating whether the key event has been handled.
	/// Set to <c>true</c> to prevent the editor from processing the key.
	/// </summary>
	public bool Handled { get; set; }
}
