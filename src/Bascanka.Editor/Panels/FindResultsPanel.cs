using Bascanka.Core.Search;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.Panels;

/// <summary>
/// Displays Find All results in a dockable bottom panel using a TreeView
/// that supports multiple search sessions with expand/collapse.
/// </summary>
public class FindResultsPanel : UserControl
{

	// ── Controls ─────────────────────────────────────────────────────
	private readonly BufferedTreeView _treeView;
	private readonly Label _headerLabel;
	private readonly Button _btnClose;
	private readonly Panel _headerPanel;
	private readonly ContextMenuStrip _contextMenu;

	// ── State ────────────────────────────────────────────────────────
	private readonly List<SearchSession> _sessions = [];
	private ITheme? _theme;
	private Font? _boldFont;
	private Font? _normalFont;

	// ── Context menu items (for localization) ─────────────────────
	private ToolStripMenuItem _menuCopyLine = null!;
	private ToolStripMenuItem _menuCopyAll = null!;
	private ToolStripMenuItem _menuCopyPath = null!;
	private ToolStripMenuItem _menuOpenInNewTab = null!;
	private ToolStripMenuItem _menuRemoveSearch = null!;
	private ToolStripMenuItem _menuClearAll = null!;

	// ── Localizable labels ────────────────────────────────────────
	private string _headerText = "Find Results";
	private string _searchesFormat = "Find Results ({0} search{1})";
	private string _matchFormat = "\"{0}\" \u2014 {1} match{2}  ({3})  [x]";
	private string _matchFilesFormat = "\"{0}\" \u2014 {1} match{2} in {3} file{4}  ({5})  [x]";

	// ── Events ───────────────────────────────────────────────────────

	/// <summary>
	/// Raised when the user double-clicks a result, indicating they
	/// want to navigate to that match in the editor.
	/// </summary>
	public event EventHandler<NavigateToResultEventArgs>? NavigateToResult;

	/// <summary>Raised when the results list is cleared.</summary>
	public event EventHandler? ResultsCleared;

	/// <summary>Raised when the user clicks the X button to close the panel.</summary>
	public event EventHandler? PanelCloseRequested;

	/// <summary>
	/// Raised when the user requests to open search results in a new tab.
	/// The string contains the formatted result lines.
	/// </summary>
	public event EventHandler<string>? OpenResultsInNewTab;

	/// <summary>Raised before a page change starts (show progress overlay).</summary>
	public event EventHandler? PageChanging;

	/// <summary>Raised after a page change completes (hide progress overlay).</summary>
	public event EventHandler? PageChanged;

	// ── Construction ─────────────────────────────────────────────────

	public FindResultsPanel()
	{
		Dock = DockStyle.Bottom;
		Height = 200;

		// ── Header panel ────────────────────────────────────────────
		_headerPanel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 28,
			Padding = new Padding(4, 4, 4, 2),
		};

		_headerLabel = new Label
		{
			AutoSize = true,
			Text = "Find Results",
			Location = new Point(4, 5),
		};

		_btnClose = new Button
		{
			Text = "\u2715",
			Width = 22,
			Height = 22,
			FlatStyle = FlatStyle.Flat,
			Anchor = AnchorStyles.Top | AnchorStyles.Right,
			Font = new Font("Segoe UI", 9f),
			Cursor = Cursors.Hand,
		};
		_btnClose.FlatAppearance.BorderSize = 0;
		_btnClose.Click += (_, _) => PanelCloseRequested?.Invoke(this, EventArgs.Empty);

		_headerPanel.Controls.Add(_btnClose);
		_headerPanel.Controls.Add(_headerLabel);
		_headerPanel.Resize += (_, _) =>
		{
			_btnClose.Location = new Point(_headerPanel.Width - _btnClose.Width - 4, 3);
		};

		// ── TreeView ────────────────────────────────────────────────
		_treeView = new BufferedTreeView
		{
			Dock = DockStyle.Fill,
			BorderStyle = BorderStyle.None,
			ShowLines = true,
			ShowPlusMinus = true,
			ShowRootLines = true,
			FullRowSelect = true,
			HideSelection = false,
			DrawMode = TreeViewDrawMode.OwnerDrawText,
			ItemHeight = 20,
		};

		_treeView.DrawNode += OnDrawNode;
		_treeView.NodeMouseDoubleClick += OnNodeDoubleClick;
		_treeView.NodeMouseClick += OnNodeMouseClick;
		_treeView.MouseDown += OnTreeViewMouseDown;
		_treeView.KeyDown += OnTreeViewKeyDown;

		// ── Context menu ────────────────────────────────────────────
		_contextMenu = BuildContextMenu();
		_treeView.ContextMenuStrip = _contextMenu;

		// ── Layout ──────────────────────────────────────────────────
		Controls.Add(_treeView);
		Controls.Add(_headerPanel);
	}

	// ── Public API ───────────────────────────────────────────────────

	/// <summary>
	/// Gets or sets whether the built-in header panel is visible.
	/// Set to <c>false</c> when the panel is hosted inside a tab strip
	/// that already provides a title and close button.
	/// </summary>
	public bool ShowHeader
	{
		get => _headerPanel.Visible;
		set => _headerPanel.Visible = value;
	}

	/// <summary>The theme used for rendering panel colours.</summary>
	public Func<ITheme, ToolStripRenderer>? ContextMenuRenderer { get; set; }

	public ITheme? Theme
	{
		get => _theme;
		set
		{
			_theme = value;
			if (value is not null && ContextMenuRenderer is not null)
				_contextMenu.Renderer = ContextMenuRenderer(value);
			ApplyTheme();
		}
	}

	/// <summary>
	/// Adds a new search session to the panel. New sessions appear at
	/// the top. Previous sessions are preserved.
	/// </summary>
	public void AddSearchResults(List<SearchResult> results, string pattern, string scopeLabel, bool multiFile = false)
	{
		ArgumentNullException.ThrowIfNull(results);

		var session = new SearchSession
		{
			Pattern = pattern,
			ScopeLabel = scopeLabel,
			Results = results,
			IsMultiFile = multiFile,
		};
		_sessions.Insert(0, session);

		BuildSessionNode(session, insertAtTop: true);
		UpdateHeaderLabel();
		Visible = true;
	}

	/// <summary>Removes all results from the panel.</summary>
	public void ClearResults()
	{
		_sessions.Clear();
		_treeView.Nodes.Clear();
		UpdateHeaderLabel();
		ResultsCleared?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Removes a specific search session by its tree node.</summary>
	public void RemoveSession(TreeNode rootNode)
	{
		if (rootNode.Tag is SearchSession session)
		{
			_sessions.Remove(session);
			_treeView.Nodes.Remove(rootNode);
			UpdateHeaderLabel();
		}
	}

	/// <summary>The current set of displayed results (all sessions combined).</summary>
	public IReadOnlyList<SearchResult> Results =>
		_sessions.SelectMany(s => s.Results).ToList().AsReadOnly();

	// ── Tree building ────────────────────────────────────────────────

	/// <summary>
	/// Maximum tree nodes to create per session to keep the UI responsive.
	/// </summary>
	private const int MaxDisplayedNodes = 10_000;

	/// <summary>
	/// Creates the TreeNode hierarchy for a session's current page.
	/// This method only creates objects — it does not touch any controls,
	/// so it can safely be called from a background thread.
	/// </summary>
	private TreeNode CreateSessionRootNode(SearchSession session)
	{
		int matchCount = session.Results.Count;
		bool resultsCapped = matchCount >= Core.Search.SearchEngine.MaxResults;
		string matchCountText = resultsCapped
			? $"{Core.Search.SearchEngine.MaxResults:N0}+"
			: matchCount.ToString("N0");

		var fileGroups = session.Results
			.Where(r => r.FilePath is not null)
			.GroupBy(r => r.FilePath!, StringComparer.OrdinalIgnoreCase)
			.ToList();

		int fileCount = fileGroups.Count;
		bool isMultiFile = fileCount > 1 || (fileCount == 1 && session.IsMultiFile);

		string rootText = isMultiFile
			? string.Format(_matchFilesFormat, session.Pattern, matchCountText, Plural(matchCount), fileCount, Plural(fileCount), session.ScopeLabel)
			: string.Format(_matchFormat, session.Pattern, matchCountText, Plural(matchCount), session.ScopeLabel);

		var rootNode = new TreeNode(rootText) { Tag = session };

		// ── Pagination ──────────────────────────────────────────────
		int pageStart = session.DisplayOffset;
		int pageEnd = Math.Min(pageStart + MaxDisplayedNodes, matchCount);

		// "Show previous" navigation node.
		if (pageStart > 0)
		{
			int prevOffset = Math.Max(0, pageStart - MaxDisplayedNodes);
			string prevText = $"\u25B2 Show previous {MaxDisplayedNodes:N0}  (results {prevOffset + 1:N0}\u2013{pageStart:N0})";
			rootNode.Nodes.Add(new TreeNode(prevText)
			{
				Tag = new LoadPageMarker { Session = session, TargetOffset = prevOffset },
			});
		}

		// ── Build match nodes for the current page ──────────────────
		if (isMultiFile)
		{
			// Slice results by page range, then group by file.
			var pageGroups = new Dictionary<string, List<SearchResult>>(StringComparer.OrdinalIgnoreCase);
			for (int i = pageStart; i < pageEnd; i++)
			{
				var r = session.Results[i];
				string key = r.FilePath ?? string.Empty;
				if (!pageGroups.TryGetValue(key, out var list))
				{
					list = [];
					pageGroups[key] = list;
				}
				list.Add(r);
			}

			foreach (var kvp in pageGroups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
			{
				string fileName = Path.GetFileName(kvp.Key);
				int groupCount = kvp.Value.Count;
				var fileNode = new TreeNode($"{fileName} ({groupCount} match{Plural(groupCount)})")
				{
					Tag = kvp.Key,
				};

				foreach (var result in kvp.Value)
				{
					fileNode.Nodes.Add(new TreeNode($"Line {result.LineNumber}: {result.LineText.TrimEnd()}")
					{
						Tag = result,
					});
				}
				rootNode.Nodes.Add(fileNode);
			}
		}
		else
		{
			for (int i = pageStart; i < pageEnd; i++)
			{
				var result = session.Results[i];
				rootNode.Nodes.Add(new TreeNode($"Line {result.LineNumber}: {result.LineText.TrimEnd()}")
				{
					Tag = result,
				});
			}
		}

		// "Show next" navigation node.
		if (pageEnd < matchCount)
		{
			int remaining = matchCount - pageEnd;
			string nextText = $"\u25BC Show next {Math.Min(MaxDisplayedNodes, remaining):N0}  " +
				$"(showing {pageStart + 1:N0}\u2013{pageEnd:N0} of {matchCountText})";
			rootNode.Nodes.Add(new TreeNode(nextText)
			{
				Tag = new LoadPageMarker { Session = session, TargetOffset = pageEnd },
			});
		}

		return rootNode;
	}

	/// <summary>
	/// Builds a session node and inserts it into the TreeView (UI thread only).
	/// Used for initial result display — not for page changes.
	/// </summary>
	private void BuildSessionNode(SearchSession session, bool insertAtTop)
	{
		var rootNode = CreateSessionRootNode(session);

		_treeView.BeginUpdate();
		if (insertAtTop)
			_treeView.Nodes.Insert(0, rootNode);
		else
			_treeView.Nodes.Add(rootNode);

		rootNode.Expand();
		foreach (TreeNode child in rootNode.Nodes)
		{
			if (child.Tag is string) // file group node
				child.Expand();
		}
		_treeView.EndUpdate();
	}

	private void UpdateHeaderLabel()
	{
		_headerLabel.Text = _sessions.Count > 0
			? string.Format(_searchesFormat, _sessions.Count, Plural(_sessions.Count))
			: _headerText;
	}

	private static string Plural(int count) => count == 1 ? "" : "es";

	// ── Custom drawing ───────────────────────────────────────────────

	private void OnDrawNode(object? sender, DrawTreeNodeEventArgs e)
	{
		if (e.Node is null || e.Bounds.IsEmpty) return;

		EnsureFonts();

		bool isRootNode = e.Node.Tag is SearchSession;
		bool isFileNode = e.Node.Tag is string;
		bool isSelected = (e.State & TreeNodeStates.Selected) != 0;

		Color bgColor = isSelected
			? (_theme?.SelectionBackground ?? SystemColors.Highlight)
			: (_theme?.EditorBackground ?? _treeView.BackColor);
		Color fgColor = isSelected
			? (_theme?.SelectionForeground ?? SystemColors.HighlightText)
			: (_theme?.EditorForeground ?? _treeView.ForeColor);

		var font = isRootNode || isFileNode ? _boldFont! : _normalFont!;
		var bounds = e.Bounds;

		using var bgBrush = new SolidBrush(bgColor);
		e.Graphics.FillRectangle(bgBrush, bounds);

		// Draw the [x] remove button on root nodes.
		if (isRootNode)
		{
			string fullText = e.Node.Text;
			int closeSep = fullText.LastIndexOf("  [x]", StringComparison.Ordinal);
			string mainText = closeSep >= 0 ? fullText[..closeSep] : fullText;
			string closeText = closeSep >= 0 ? fullText[closeSep..] : string.Empty;

			TextRenderer.DrawText(e.Graphics, mainText, font, bounds.Location, fgColor,
				TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

			if (closeText.Length > 0)
			{
				var mainSize = TextRenderer.MeasureText(mainText, font);
				var closePoint = new Point(bounds.X + mainSize.Width - 4, bounds.Y);
				Color closeColor = isSelected ? fgColor : Color.FromArgb(180, 80, 80);
				TextRenderer.DrawText(e.Graphics, closeText, _normalFont!, closePoint, closeColor,
					TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
			}
		}
		else if (e.Node.Tag is SearchResult result)
		{
			// Draw match nodes: line number in accent color, text in normal color.
			string linePrefix = $"Line {result.LineNumber}: ";
			string lineText = result.LineText.Trim();

			Color lineNumColor = isSelected ? fgColor :
				(_theme?.GutterForeground ?? Color.FromArgb(120, 140, 160));

			TextRenderer.DrawText(e.Graphics, linePrefix, _normalFont!, bounds.Location, lineNumColor,
				TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

			var prefixSize = TextRenderer.MeasureText(linePrefix, _normalFont!);
			var textPoint = new Point(bounds.X + prefixSize.Width - 4, bounds.Y);
			TextRenderer.DrawText(e.Graphics, lineText, _normalFont!, textPoint, fgColor,
				TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
		}
		else
		{
			// File nodes and other nodes.
			using var fgBrush = new SolidBrush(fgColor);
			TextRenderer.DrawText(e.Graphics, e.Node.Text, font, bounds.Location, fgColor,
				TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
		}
	}

	private void EnsureFonts()
	{
		if (_normalFont is null || _normalFont.Size != _treeView.Font.Size)
		{
			_boldFont?.Dispose();
			_normalFont?.Dispose();
			_normalFont = new Font(_treeView.Font, FontStyle.Regular);
			_boldFont = new Font(_treeView.Font, FontStyle.Bold);
		}
	}

	// ── Event handlers ───────────────────────────────────────────────

	private void OnTreeViewMouseDown(object? sender, MouseEventArgs e)
	{
		// Select the node under the cursor on right-click so the context
		// menu always targets the correct node.
		if (e.Button == MouseButtons.Right)
		{
			var node = _treeView.GetNodeAt(e.X, e.Y);
			if (node is not null)
				_treeView.SelectedNode = node;
		}
	}

	/// <summary>
	/// Handles a page change asynchronously: fires <see cref="PageChanging"/>,
	/// builds TreeNodes on a background thread, then batch-inserts them into
	/// the TreeView with periodic yields so the spinner stays animated.
	/// </summary>
	private async void HandlePageChangeAsync(LoadPageMarker marker)
	{
		bool forward = marker.TargetOffset > marker.Session.DisplayOffset;
		var session = marker.Session;
		session.DisplayOffset = Math.Max(0, marker.TargetOffset);

		PageChanging?.Invoke(this, EventArgs.Empty);

		// Build the node hierarchy off the UI thread.
		var rootNode = await Task.Run(() => CreateSessionRootNode(session));

		// Extract children so we can batch-add them to avoid
		// a single long block of TVM_INSERTITEM messages.
		var children = new TreeNode[rootNode.Nodes.Count];
		rootNode.Nodes.CopyTo(children, 0);
		rootNode.Nodes.Clear();

		// Find the old session node.
		int nodeIndex = -1;
		for (int i = 0; i < _treeView.Nodes.Count; i++)
		{
			if (_treeView.Nodes[i].Tag == session)
			{
				nodeIndex = i;
				break;
			}
		}

		if (nodeIndex < 0)
		{
			PageChanged?.Invoke(this, EventArgs.Empty);
			return;
		}

		// Remove old root, insert new empty root, expand it (instant — no children).
		_treeView.BeginUpdate();
		_treeView.Nodes.RemoveAt(nodeIndex);
		_treeView.Nodes.Insert(nodeIndex, rootNode);
		rootNode.Expand();
		_treeView.EndUpdate();

		// Batch-add children with BeginUpdate active (no TreeView repaints).
		// The spinner overlay is a separate HWND so it still animates
		// during the Task.Delay yields between batches.
		const int BatchSize = 500;
		_treeView.BeginUpdate();
		try
		{
			for (int i = 0; i < children.Length; i += BatchSize)
			{
				int end = Math.Min(i + BatchSize, children.Length);
				for (int j = i; j < end; j++)
				{
					rootNode.Nodes.Add(children[j]);
					if (children[j].Tag is string) // file group node
						children[j].Expand();
				}

				// Yield to message loop so the spinner can animate.
				if (end < children.Length)
					await Task.Delay(1);
			}

			// Select the appropriate edge node.
			if (rootNode.Nodes.Count > 0)
			{
				TreeNode target = forward
					? rootNode.Nodes[0]
					: rootNode.Nodes[^1];
				_treeView.SelectedNode = target;
				target.EnsureVisible();
			}
		}
		finally
		{
			_treeView.EndUpdate();
		}

		PageChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
	{
		if (e.Node?.Tag is LoadPageMarker marker)
		{
			HandlePageChangeAsync(marker);
			return;
		}

		if (e.Node?.Tag is SearchResult result)
		{
			NavigateToResult?.Invoke(this, new NavigateToResultEventArgs(result));
		}
	}

	private void OnNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
	{
		// Navigate pages on single-click for "Show next/previous" nodes.
		if (e.Node?.Tag is LoadPageMarker marker && e.Button == MouseButtons.Left)
		{
			HandlePageChangeAsync(marker);
			return;
		}

		// Check if user clicked on the [x] area of a root node.
		if (e.Node?.Tag is SearchSession && e.Button == MouseButtons.Left)
		{
			EnsureFonts();
			string fullText = e.Node.Text;
			int closeSep = fullText.LastIndexOf("  [x]", StringComparison.Ordinal);
			if (closeSep < 0) return;

			string mainText = fullText[..closeSep];
			var mainSize = TextRenderer.MeasureText(mainText, _boldFont!);
			int closeStartX = e.Node.Bounds.X + mainSize.Width - 4;

			if (e.X >= closeStartX)
			{
				RemoveSession(e.Node);
			}
		}
	}

	private void OnTreeViewKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Enter)
		{
			e.SuppressKeyPress = true;
			if (_treeView.SelectedNode?.Tag is LoadPageMarker marker)
			{
				HandlePageChangeAsync(marker);
			}
			else if (_treeView.SelectedNode?.Tag is SearchResult result)
			{
				NavigateToResult?.Invoke(this, new NavigateToResultEventArgs(result));
			}
		}
		else if (e.KeyCode == Keys.Delete)
		{
			e.SuppressKeyPress = true;
			var node = _treeView.SelectedNode;
			if (node?.Tag is SearchSession)
				RemoveSession(node);
		}
		else if (e.KeyCode == Keys.C && e.Control)
		{
			e.SuppressKeyPress = true;
			CopySelectedResults();
		}
	}

	// ── Copy / export helpers ────────────────────────────────────────

	/// <summary>
	/// Copies the selected node's results to the clipboard.
	/// Match node → single line. File node → all matches in that file.
	/// Root node → all matches in the session.
	/// </summary>
	private void CopySelectedResults()
	{
		var node = _treeView.SelectedNode;
		if (node is null) return;

		string text = CollectResultLines(node);
		if (!string.IsNullOrEmpty(text))
			Clipboard.SetText(text);
	}

	/// <summary>
	/// Collects match lines from a node depending on its type.
	/// </summary>
	private static string CollectResultLines(TreeNode node)
	{
		if (node.Tag is SearchResult result)
			return $"Line {result.LineNumber}: {result.LineText.Trim()}";

		// File node or root node — gather all SearchResult children.
		var lines = new List<string>();
		CollectResultLinesRecursive(node, lines);
		return string.Join(Environment.NewLine, lines);
	}

	private static void CollectResultLinesRecursive(TreeNode node, List<string> lines)
	{
		if (node.Tag is SearchResult r)
		{
			lines.Add(r.LineText.Trim());
			return;
		}

		foreach (TreeNode child in node.Nodes)
			CollectResultLinesRecursive(child, lines);
	}

	/// <summary>
	/// Formats all results in a session as pure text lines (no line numbers).
	/// </summary>
	private static string FormatSessionAsText(SearchSession session)
	{
		var lines = session.Results
			.OrderBy(r => r.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
			.ThenBy(r => r.LineNumber)
			.Select(r => r.LineText.Trim());

		return string.Join(Environment.NewLine, lines);
	}

	// ── Context menu ─────────────────────────────────────────────────

	private ContextMenuStrip BuildContextMenu()
	{
		var menu = new ContextMenuStrip();

		_menuCopyLine = new ToolStripMenuItem("Copy Line")
		{
			ShortcutKeyDisplayString = "Ctrl+C"
		};
		_menuCopyLine.Click += (_, _) => CopySelectedResults();

		_menuCopyAll = new ToolStripMenuItem("Copy All Results");
		_menuCopyAll.Click += (_, _) =>
		{
			var rootNode = GetRootNode(_treeView.SelectedNode);
			if (rootNode?.Tag is SearchSession session)
			{
				var lines = new List<string>();
				foreach (var r in session.Results.OrderBy(r => r.LineNumber))
					lines.Add(r.LineText.Trim());
				if (lines.Count > 0)
					Clipboard.SetText(string.Join(Environment.NewLine, lines));
			}
		};

		_menuCopyPath = new ToolStripMenuItem("Copy Path");
		_menuCopyPath.Click += (_, _) =>
		{
			if (_treeView.SelectedNode?.Tag is SearchResult r && r.FilePath is not null)
				Clipboard.SetText(r.FilePath);
			else if (_treeView.SelectedNode?.Tag is string path)
				Clipboard.SetText(path);
		};

		_menuOpenInNewTab = new ToolStripMenuItem("Open Results in New Tab");
		_menuOpenInNewTab.Click += (_, _) =>
		{
			var rootNode = GetRootNode(_treeView.SelectedNode);
			if (rootNode?.Tag is SearchSession session)
			{
				string text = FormatSessionAsText(session);
				OpenResultsInNewTab?.Invoke(this, text);
			}
		};

		_menuRemoveSearch = new ToolStripMenuItem("Remove Search");
		_menuRemoveSearch.Click += (_, _) =>
		{
			var node = GetRootNode(_treeView.SelectedNode);
			if (node?.Tag is SearchSession)
				RemoveSession(node);
		};

		_menuClearAll = new ToolStripMenuItem("Clear All");
		_menuClearAll.Click += (_, _) => ClearResults();

		menu.Items.AddRange([_menuCopyLine, _menuCopyAll, _menuCopyPath,
			new ToolStripSeparator(),
			_menuOpenInNewTab,
			new ToolStripSeparator(),
			_menuRemoveSearch, _menuClearAll]);

		menu.Opening += (_, _) =>
		{
			var node = _treeView.SelectedNode;
			bool hasSession = GetRootNode(node)?.Tag is SearchSession;
			_menuCopyLine.Enabled = node is not null;
			_menuCopyAll.Enabled = hasSession;
			_menuCopyPath.Enabled = (node?.Tag is SearchResult r && r.FilePath is not null)
				|| node?.Tag is string;
			_menuOpenInNewTab.Enabled = hasSession;
			_menuRemoveSearch.Enabled = hasSession;
		};

		return menu;
	}

	/// <summary>
	/// Updates context menu, header, and node format text for localization.
	/// </summary>
	public void SetMenuTexts(string copyLine, string copyAllResults, string copyPath,
		string openInNewTab, string removeSearch, string clearAll,
		string headerText, string headerWithCountFormat,
		string matchFormat, string matchFilesFormat)
	{
		_menuCopyLine.Text = copyLine;
		_menuCopyAll.Text = copyAllResults;
		_menuCopyPath.Text = copyPath;
		_menuOpenInNewTab.Text = openInNewTab;
		_menuRemoveSearch.Text = removeSearch;
		_menuClearAll.Text = clearAll;
		_headerText = headerText;
		_searchesFormat = headerWithCountFormat;
		_matchFormat = matchFormat;
		_matchFilesFormat = matchFilesFormat;
		UpdateHeaderLabel();
	}

	private static TreeNode? GetRootNode(TreeNode? node)
	{
		while (node?.Parent is not null)
			node = node.Parent;
		return node;
	}

	// ── Theme ────────────────────────────────────────────────────────

	private void ApplyTheme()
	{
		if (_theme is null) return;

		BackColor = _theme.FindPanelBackground;
		ForeColor = _theme.FindPanelForeground;

		_headerPanel.BackColor = _theme.FindPanelBackground;
		_headerLabel.ForeColor = _theme.FindPanelForeground;

		_btnClose.BackColor = _theme.FindPanelBackground;
		_btnClose.ForeColor = _theme.FindPanelForeground;
		_btnClose.FlatAppearance.BorderSize = 0;

		_treeView.BackColor = _theme.EditorBackground;
		_treeView.ForeColor = _theme.EditorForeground;

		// Reset fonts to pick up any changes.
		_boldFont?.Dispose();
		_normalFont?.Dispose();
		_boldFont = null;
		_normalFont = null;

		Invalidate(true);
	}

	// ── Disposal ─────────────────────────────────────────────────────

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_boldFont?.Dispose();
			_normalFont?.Dispose();
			_contextMenu.Dispose();
		}
		base.Dispose(disposing);
	}
}
