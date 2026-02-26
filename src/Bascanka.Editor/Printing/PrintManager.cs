using System.Drawing;
using System.Drawing.Printing;
using Bascanka.Core.Buffer;
using Bascanka.Core.Syntax;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Printing;

/// <summary>
/// Coordinates printing a <see cref="PieceTable"/> document with optional
/// syntax highlighting through a <see cref="PrintDocument"/>.  Handles
/// page layout, header (filename + page number), footer (date), and
/// colour-mapped token rendering.
/// </summary>
public sealed class PrintManager
{
	// ── Fields ──────────────────────────────────────────────────────────

	private readonly PrintLayoutEngine _layoutEngine = new();

	// State held across PrintPage events.
	private PieceTable? _buffer;
	private ILexer? _lexer;
	private ITheme? _theme;
	private Font? _font;
	private string _fileName = "Untitled";
	private PrintLayout? _layout;
	private List<PageRange>? _pages;
	private int _currentPageIndex;
	private bool _wordWrap;

	// ── Public API ──────────────────────────────────────────────────────

	/// <summary>
	/// Whether to wrap long lines when printing. Default is <see langword="false"/>.
	/// </summary>
	public bool WordWrap
	{
		get => _wordWrap;
		set => _wordWrap = value;
	}

	/// <summary>
	/// Initiates a print job.  Call this method to wire up the
	/// <see cref="PrintDocument"/> and begin printing.
	/// </summary>
	/// <param name="doc">The <see cref="PrintDocument"/> to print to.</param>
	/// <param name="buffer">The text buffer containing the document content.</param>
	/// <param name="lexer">
	/// An optional lexer for syntax highlighting.  Pass <see langword="null"/>
	/// to print without colour.
	/// </param>
	/// <param name="theme">
	/// The colour theme used to map tokens to foreground colours.
	/// </param>
	/// <param name="font">The monospaced font to use for printing.</param>
	/// <param name="fileName">
	/// The file name displayed in the page header.
	/// </param>
	public void Print(
		PrintDocument doc,
		PieceTable buffer,
		ILexer? lexer,
		ITheme theme,
		Font font,
		string? fileName = null)
	{
		ArgumentNullException.ThrowIfNull(doc);
		ArgumentNullException.ThrowIfNull(buffer);
		ArgumentNullException.ThrowIfNull(theme);
		ArgumentNullException.ThrowIfNull(font);

		_buffer = buffer;
		_lexer = lexer;
		_theme = theme;
		_font = font;
		_fileName = fileName ?? "Untitled";
		_currentPageIndex = 0;
		_layout = null;
		_pages = null;

		doc.BeginPrint += OnBeginPrint;
		doc.PrintPage += OnPrintPage;
		doc.EndPrint += OnEndPrint;

		doc.Print();
	}

	/// <summary>
	/// Generates preview data for all pages without actually printing.
	/// </summary>
	public List<PagePreviewData> GeneratePreview(
		PieceTable buffer,
		ILexer? lexer,
		ITheme theme,
		Font font,
		PageSettings pageSettings)
	{
		ArgumentNullException.ThrowIfNull(buffer);
		ArgumentNullException.ThrowIfNull(theme);
		ArgumentNullException.ThrowIfNull(font);
		ArgumentNullException.ThrowIfNull(pageSettings);

		PrintLayout layout = PrintLayoutEngine.CalculateLayout(pageSettings, font);

		string[] allLines = GetAllLines(buffer);
		int[] lineLengths = [.. allLines.Select(l => l.Length)];

		List<PageRange> pages = PrintLayoutEngine.CalculatePageBreaks(
			allLines.Length, layout.LinesPerPage, _wordWrap, lineLengths, layout.CharsPerLine);

		var previews = new List<PagePreviewData>(pages.Count);
		LexerState state = LexerState.Normal;

		// Pre-tokenize all lines so we can slice by page.
		var allTokens = new List<List<Token>>(allLines.Length);
		for (int i = 0; i < allLines.Length; i++)
		{
			if (lexer is not null)
			{
				var (tokens, endState) = lexer.Tokenize(allLines[i], state);
				allTokens.Add(tokens);
				state = endState;
			}
			else
			{
				allTokens.Add([]);
			}
		}

		for (int p = 0; p < pages.Count; p++)
		{
			PageRange range = pages[p];
			var preview = new PagePreviewData
			{
				PageNumber = p + 1,
				TotalPages = pages.Count,
			};

			for (int line = range.StartLine; line <= range.EndLine && line < allLines.Length; line++)
			{
				preview.Lines.Add(allLines[line]);
				preview.LineTokens.Add(allTokens[line]);
			}

			previews.Add(preview);
		}

		return previews;
	}

	// ── PrintDocument event handlers ────────────────────────────────────

	private void OnBeginPrint(object? sender, PrintEventArgs e)
	{
		_currentPageIndex = 0;
	}

	private void OnPrintPage(object? sender, PrintPageEventArgs e)
	{
		if (_buffer is null || _theme is null || _font is null || e.Graphics is null)
		{
			e.HasMorePages = false;
			return;
		}

		Graphics g = e.Graphics;

		// Compute layout on first page.
		if (_layout is null || _pages is null)
		{
			PageSettings pageSettings = e.PageSettings ?? ((PrintDocument)sender!).DefaultPageSettings;
			_layout = PrintLayoutEngine.CalculateLayout(pageSettings, _font);

			string[] allLines = GetAllLines(_buffer);
			int[] lineLengths = [.. allLines.Select(l => l.Length)];

			_pages = PrintLayoutEngine.CalculatePageBreaks(allLines.Length, _layout.LinesPerPage, _wordWrap, lineLengths, _layout.CharsPerLine);
		}

		if (_currentPageIndex >= _pages.Count)
		{
			e.HasMorePages = false;
			return;
		}

		PageRange range = _pages[_currentPageIndex];

		// ---- Header ----
		DrawHeader(g, _layout, _currentPageIndex + 1, _pages.Count);

		// ---- Footer ----
		DrawFooter(g, _layout);

		// ---- Body (lines with syntax highlighting) ----
		DrawBody(g, _layout, range);

		_currentPageIndex++;
		e.HasMorePages = _currentPageIndex < _pages.Count;
	}

	private void OnEndPrint(object? sender, PrintEventArgs e)
	{
		// Unhook to avoid leaking state if the PrintDocument is reused.
		if (sender is PrintDocument doc)
		{
			doc.BeginPrint -= OnBeginPrint;
			doc.PrintPage -= OnPrintPage;
			doc.EndPrint -= OnEndPrint;
		}

		_buffer = null;
		_lexer = null;
		_theme = null;
		_font = null;
		_layout = null;
		_pages = null;
	}

	// ── Drawing helpers ─────────────────────────────────────────────────

	private void DrawHeader(Graphics g, PrintLayout layout, int pageNumber, int totalPages)
	{
		if (_theme is null || _font is null) return;

		using Font headerFont = new(_font.FontFamily, _font.Size, FontStyle.Bold);
		using Brush brush = new SolidBrush(Color.Black);
		using StringFormat leftFormat = new() { Alignment = StringAlignment.Near };
		using StringFormat rightFormat = new() { Alignment = StringAlignment.Far };

		g.DrawString(_fileName, headerFont, brush, layout.HeaderArea, leftFormat);
		g.DrawString($"Page {pageNumber} of {totalPages}", headerFont, brush,
			layout.HeaderArea, rightFormat);

		// Separator line below header.
		float lineY = layout.HeaderArea.Bottom + 2;
		using Pen pen = new(Color.Gray, 0.5f);
		g.DrawLine(pen, layout.HeaderArea.Left, lineY, layout.HeaderArea.Right, lineY);
	}

	private void DrawFooter(Graphics g, PrintLayout layout)
	{
		if (_font is null) return;

		using Brush brush = new SolidBrush(Color.Gray);
		using StringFormat centerFormat = new() { Alignment = StringAlignment.Center };

		string dateStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
		g.DrawString(dateStr, _font, brush, layout.FooterArea, centerFormat);

		// Separator line above footer.
		float lineY = layout.FooterArea.Top - 2;
		using Pen pen = new(Color.Gray, 0.5f);
		g.DrawLine(pen, layout.FooterArea.Left, lineY, layout.FooterArea.Right, lineY);
	}

	private void DrawBody(Graphics g, PrintLayout layout, PageRange range)
	{
		if (_buffer is null || _theme is null || _font is null) return;

		using StringFormat sf = new(StringFormat.GenericTypographic)
		{
			FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap,
		};

		float y = layout.PrintableArea.Top;
		float x = layout.PrintableArea.Left;

		// Compute lexer state up to the first line on this page.
		LexerState state = LexerState.Normal;
		if (_lexer is not null && range.StartLine > 0)
		{
			for (int i = 0; i < range.StartLine; i++)
			{
				string lineText = i < _buffer.LineCount ? _buffer.GetLine(i) : "";
				(_, state) = _lexer.Tokenize(lineText, state);
			}
		}

		for (int line = range.StartLine; line <= range.EndLine; line++)
		{
			if (line >= _buffer.LineCount) break;

			string lineText = _buffer.GetLine(line);

			if (_lexer is not null)
			{
				var (tokens, endState) = _lexer.Tokenize(lineText, state);
				state = endState;

				// Render each token with its colour.
				DrawTokenizedLine(g, sf, tokens, lineText, x, y);
			}
			else
			{
				// No lexer: render as plain text.
				using Brush plainBrush = new SolidBrush(Color.Black);
				string printLine = TruncateOrWrap(lineText, layout.CharsPerLine);
				g.DrawString(printLine, _font, plainBrush, x, y, sf);
			}

			y += layout.LineHeight;
		}
	}

	private void DrawTokenizedLine(
		Graphics g,
		StringFormat sf,
		List<Token> tokens,
		string lineText,
		float x,
		float y)
	{
		if (_theme is null || _font is null) return;

		if (tokens.Count == 0)
		{
			// No tokens -- render the whole line in default colour.
			using Brush defaultBrush = new SolidBrush(Color.Black);
			g.DrawString(lineText, _font, defaultBrush, x, y, sf);
			return;
		}

		float currentX = x;

		foreach (Token token in tokens)
		{
			if (token.Start >= lineText.Length) break;

			int end = Math.Min(token.End, lineText.Length);
			string span = lineText[token.Start..end];

			Color color = _theme.GetTokenColor(token.Type);
			using Brush brush = new SolidBrush(color);
			g.DrawString(span, _font, brush, currentX, y, sf);

			SizeF size = g.MeasureString(span, _font, 0, sf);
			currentX += size.Width;
		}
	}

	// ── Utility ─────────────────────────────────────────────────────────

	private static string[] GetAllLines(PieceTable buffer)
	{
		long lineCount = buffer.LineCount;
		var lines = new string[lineCount];
		for (long i = 0; i < lineCount; i++)
			lines[i] = buffer.GetLine(i);
		return lines;
	}

	private static string TruncateOrWrap(string line, int charsPerLine)
	{
		// For the simple (non-wrap) case, truncate long lines.
		if (line.Length <= charsPerLine)
			return line;
		return line[..charsPerLine];
	}
}
