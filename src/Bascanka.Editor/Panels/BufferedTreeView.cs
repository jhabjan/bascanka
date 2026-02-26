namespace Bascanka.Editor.Panels;


internal sealed class BufferedTreeView : TreeView
{
	public BufferedTreeView()
	{
		SetStyle(
			ControlStyles.OptimizedDoubleBuffer |
			ControlStyles.AllPaintingInWmPaint,
			true);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		// Enable native double-buffering via TVM_SETEXTENDEDSTYLE.
		const int TVM_SETEXTENDEDSTYLE = 0x112C;
		const int TVS_EX_DOUBLEBUFFER = 0x0004;
		SendMessage(Handle, TVM_SETEXTENDEDSTYLE, TVS_EX_DOUBLEBUFFER, TVS_EX_DOUBLEBUFFER);
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
}

