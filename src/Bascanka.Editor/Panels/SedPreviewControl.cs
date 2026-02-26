using Bascanka.Core.Diff;
using Bascanka.Editor.Controls;
using Bascanka.Editor.Themes;
using static Enums;

namespace Bascanka.Editor.Panels;

/// <summary>
/// A composite control that displays the result of a sed transformation
/// with an Apply/Discard toolbar and a read-only editor preview.
/// </summary>
public sealed class SedPreviewControl : UserControl
{
	private readonly EditorControl _editor;
	private readonly Panel _toolbar;
	private readonly ToolbarButton _applyButton;
	private readonly ToolbarButton _discardButton;
	private readonly Label _infoLabel;

	private Color _separatorColor;

	/// <summary>Index of the source tab this preview was created from.</summary>
	public int SourceTabIndex { get; private set; }

	/// <summary>The full transformed text.</summary>
	public string TransformedText { get; private set; } = string.Empty;

	/// <summary>Raised when the user clicks Apply.</summary>
	public event Action<SedPreviewControl>? ApplyRequested;

	/// <summary>Raised when the user clicks Discard.</summary>
	public event Action<SedPreviewControl>? DiscardRequested;

	public SedPreviewControl()
	{
		SuspendLayout();

		// ── Toolbar ──────────────────────────────────────────────────
		_toolbar = new Panel
		{
			Dock = DockStyle.Top,
			Height = 36,
			Padding = new Padding(8, 0, 8, 0),
		};
		_toolbar.Paint += PaintToolbarBorder;

		_applyButton = new ToolbarButton
		{
			Text = "\u2714  Apply",
			Height = 26,
			Top = 5,
			Left = 8,
			Cursor = Cursors.Hand,
		};
		_applyButton.Click += (_, _) => ApplyRequested?.Invoke(this);

		_discardButton = new ToolbarButton
		{
			Text = "\u2716  Discard",
			Height = 26,
			Top = 5,
			Cursor = Cursors.Hand,
		};
		_discardButton.Click += (_, _) => DiscardRequested?.Invoke(this);

		_infoLabel = new Label
		{
			AutoSize = true,
			Top = 10,
			TextAlign = ContentAlignment.MiddleLeft,
		};

		_toolbar.Controls.AddRange([_applyButton, _discardButton, _infoLabel]);

		// ── Editor ───────────────────────────────────────────────────
		_editor = new EditorControl
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
		};

		Controls.Add(_editor);
		Controls.Add(_toolbar);

		ResumeLayout(true);

		LayoutToolbarButtons();
	}

	/// <summary>
	/// Loads the preview with transformed text and metadata.
	/// </summary>
	public void LoadPreview(string sedExpression, string transformedText,
		int replacementCount, int sourceTabIndex)
	{
		SourceTabIndex = sourceTabIndex;
		TransformedText = transformedText;

		_editor.LoadText(transformedText);
		_editor.ReadOnly = true;

		_infoLabel.Text = $"\"{sedExpression}\" \u2014 {replacementCount} replacement(s)";
	}

	/// <summary>
	/// Loads the preview with exact replacement highlighting and syntax highlighting.
	/// </summary>
	public void LoadPreview(string sedExpression, string transformedText,
		int replacementCount, int sourceTabIndex,
		string? language, List<(int Start, int Length)> replacementRanges)
	{
		SourceTabIndex = sourceTabIndex;
		TransformedText = transformedText;

		_editor.LoadText(transformedText);
		_editor.ReadOnly = true;

		if (!string.IsNullOrEmpty(language))
			_editor.Language = language;

		_editor.DiffLineMarkers = BuildMarkersFromRanges(transformedText, replacementRanges);

		_infoLabel.Text = $"\"{sedExpression}\" \u2014 {replacementCount} replacement(s)";
	}

	/// <summary>
	/// Converts global replacement ranges into per-line DiffLine markers
	/// with CharDiffs highlighting only the replaced text.
	/// </summary>
	private static DiffLine[] BuildMarkersFromRanges(
		string text, List<(int Start, int Length)> ranges)
	{
		// Find line boundaries.
		var lineStarts = new List<int> { 0 };
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '\n')
				lineStarts.Add(i + 1);
		}

		int lineCount = lineStarts.Count;
		var markers = new DiffLine[lineCount];
		int rangeIdx = 0;

		for (int line = 0; line < lineCount; line++)
		{
			int lineStart = lineStarts[line];
			int lineEnd = line + 1 < lineCount
				? lineStarts[line + 1] - 1   // exclude \n
				: text.Length;

			// Collect char diffs that overlap this line.
			List<CharDiffRange>? charDiffs = null;

			while (rangeIdx < ranges.Count)
			{
				var (rStart, rLen) = ranges[rangeIdx];
				int rEnd = rStart + rLen;

				if (rStart >= lineEnd + 1) // +1 to skip past \n
					break; // this and all subsequent ranges are on later lines

				// Compute overlap with this line's content [lineStart, lineEnd).
				int localStart = Math.Max(0, rStart - lineStart);
				int localEnd = Math.Min(lineEnd - lineStart, rEnd - lineStart);
				if (localEnd > localStart)
				{
					charDiffs ??= [];
					charDiffs.Add(new CharDiffRange(localStart, localEnd - localStart));
				}

				if (rEnd <= lineEnd + 1) // range fully consumed on this line
					rangeIdx++;
				else
					break; // range continues on next line
			}

			markers[line] = new DiffLine
			{
				Type = DiffLineType.Equal,
				Text = string.Empty,
				OriginalLineNumber = line,
				CharDiffs = charDiffs,
			};
		}

		return markers;
	}

	/// <summary>
	/// Updates button labels from localized strings.
	/// </summary>
	public void SetButtonLabels(string applyText, string discardText, string countFormat, string expression, int count)
	{
		_applyButton.Text = $"\u2714  {applyText}";
		_discardButton.Text = $"\u2716  {discardText}";
		_infoLabel.Text = $"\"{expression}\" \u2014 {string.Format(countFormat, count)}";
		LayoutToolbarButtons();
	}

	/// <summary>
	/// Applies theme colours to the toolbar and the editor.
	/// </summary>
	public void ApplyTheme(ITheme theme)
	{
		_editor.Theme = theme;

		_separatorColor = Color.FromArgb(40, theme.TabActiveForeground);

		_toolbar.BackColor = theme.TabBarBackground;
		_infoLabel.ForeColor = theme.TabInactiveForeground;
		_infoLabel.BackColor = theme.TabBarBackground;

		_applyButton.NormalBg = theme.TabBarBackground;
		_applyButton.HoverBg = theme.TabInactiveBackground;
		_applyButton.ForeColor = theme.TabActiveForeground;
		_applyButton.BackColor = theme.TabBarBackground;
		_applyButton.BorderColor = Color.FromArgb(60, theme.TabActiveForeground);
		_applyButton.Invalidate();

		_discardButton.NormalBg = theme.TabBarBackground;
		_discardButton.HoverBg = theme.TabInactiveBackground;
		_discardButton.ForeColor = theme.TabActiveForeground;
		_discardButton.BackColor = theme.TabBarBackground;
		_discardButton.BorderColor = Color.FromArgb(60, theme.TabActiveForeground);
		_discardButton.Invalidate();

		_toolbar.Invalidate();
	}

	private void LayoutToolbarButtons()
	{
		using var g = CreateGraphics();
		var font = _applyButton.Font;

		int applyW = TextRenderer.MeasureText(g, _applyButton.Text, font).Width + 24;
		int discardW = TextRenderer.MeasureText(g, _discardButton.Text, font).Width + 24;

		_applyButton.Width = applyW;
		_applyButton.Left = 8;

		_discardButton.Width = discardW;
		_discardButton.Left = _applyButton.Right + 6;

		_infoLabel.Left = _discardButton.Right + 12;
	}

	private void PaintToolbarBorder(object? sender, PaintEventArgs e)
	{
		if (sender is not Panel panel) return;
		int y = panel.Height - 1;
		using var pen = new Pen(_separatorColor);
		e.Graphics.DrawLine(pen, 0, y, panel.Width, y);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_editor.Dispose();
			_toolbar.Dispose();
		}
		base.Dispose(disposing);
	}
}
