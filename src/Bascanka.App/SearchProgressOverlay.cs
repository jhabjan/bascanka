namespace Bascanka.App;


/// <summary>
/// A centered overlay panel that shows a spinning circle and a label.
/// Displayed over the form during long-running operations.
/// </summary>
internal sealed class SearchProgressOverlay : Panel
{
	private readonly Label _label;
	private readonly Button _cancelButton;
	private readonly System.Windows.Forms.Timer _spinTimer;
	private int _spinAngle;
	private const int SpinnerSize = 36;
	private const int DotCount = 10;
	private readonly Rectangle _spinnerRect;

	/// <summary>Set by the caller before showing; invoked when the user clicks Cancel or presses Escape.</summary>
	public Action? CancelRequested { get; set; }

	public SearchProgressOverlay()
	{
		Size = new Size(200, 130);
		BorderStyle = BorderStyle.FixedSingle;
		BackColor = Color.FromArgb(45, 45, 48);
		DoubleBuffered = true;

		// Spinner is drawn in OnPaint — reserve space at the top.
		_spinnerRect = new Rectangle((200 - SpinnerSize) / 2, 12, SpinnerSize, SpinnerSize);

		_label = new Label
		{
			Text = "Searching...",
			ForeColor = Color.FromArgb(220, 220, 220),
			Font = new Font("Segoe UI", 9.5f),
			TextAlign = ContentAlignment.MiddleCenter,
			Location = new Point(0, 56),
			Size = new Size(200, 24),
		};

		_cancelButton = new Button
		{
			Text = "Cancel",
			Width = 80,
			Height = 28,
			FlatStyle = FlatStyle.Flat,
			BackColor = Color.FromArgb(60, 60, 65),
			ForeColor = Color.FromArgb(220, 220, 220),
			Cursor = Cursors.Hand,
			Top = 88,
			Left = 60,
		};
		_cancelButton.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 95);
		_cancelButton.Click += (_, _) => CancelRequested?.Invoke();

		Controls.Add(_cancelButton);
		Controls.Add(_label);

		_spinTimer = new System.Windows.Forms.Timer { Interval = 80 };
		_spinTimer.Tick += (_, _) =>
		{
			_spinAngle = (_spinAngle + 1) % DotCount;
			Invalidate(_spinnerRect);
		};
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		var g = e.Graphics;
		g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

		float cx = _spinnerRect.X + SpinnerSize / 2f;
		float cy = _spinnerRect.Y + SpinnerSize / 2f;
		float radius = SpinnerSize / 2f - 4;

		for (int i = 0; i < DotCount; i++)
		{
			// Dots go clockwise; the "active" dot is brightest,
			// trailing dots fade out.
			int age = (_spinAngle - i + DotCount) % DotCount;
			int alpha = Math.Max(40, 255 - age * 24);
			float dotRadius = age == 0 ? 3.5f : 2.5f;

			double angle = 2 * Math.PI * i / DotCount - Math.PI / 2;
			float x = cx + radius * (float)Math.Cos(angle);
			float y = cy + radius * (float)Math.Sin(angle);

			using var brush = new SolidBrush(Color.FromArgb(alpha, 100, 180, 255));
			g.FillEllipse(brush, x - dotRadius, y - dotRadius, dotRadius * 2, dotRadius * 2);
		}
	}

	/// <summary>Centers the overlay on the parent form and makes it visible.</summary>
	public void ShowOverlay(Form parent, string? message = null)
	{
		_label.Text = message ?? "Searching...";

		int x = (parent.ClientSize.Width - Width) / 2;
		int y = (parent.ClientSize.Height - Height) / 2;
		Location = new Point(Math.Max(0, x), Math.Max(0, y));

		_spinAngle = 0;
		_spinTimer.Start();
		Visible = true;
		BringToFront();
		_cancelButton.Focus();
	}

	/// <summary>Hides the overlay.</summary>
	public void HideOverlay()
	{
		_spinTimer.Stop();
		Visible = false;
	}

	/// <summary>Updates the label text with a progress percentage.</summary>
	public void UpdateProgress(int percent)
	{
		percent = Math.Clamp(percent, 0, 100);
		_label.Text = $"Searching... {percent}%";
		_label.Refresh();
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (keyData == Keys.Escape)
		{
			CancelRequested?.Invoke();
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
			_spinTimer.Dispose();
		base.Dispose(disposing);
	}
}

