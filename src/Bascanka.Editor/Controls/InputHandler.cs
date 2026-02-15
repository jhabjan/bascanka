using Bascanka.Core.Buffer;
using Bascanka.Core.Commands;
using Bascanka.Editor.Macros;

namespace Bascanka.Editor.Controls;

/// <summary>
/// Processes keyboard input and translates key combinations into editing
/// operations.  All text modifications are routed through <see cref="CommandHistory"/>
/// to support undo/redo.
/// </summary>
public sealed class InputHandler
{
    private PieceTable? _document;
    private CaretManager? _caret;
    private SelectionManager? _selection;
    private CommandHistory? _history;
    private FoldingManager? _folding;
    private bool _insertMode = true; // true = Insert, false = Overwrite
    private int _tabSize = EditorControl.DefaultTabWidth;

    /// <summary>Raised after text has been modified.</summary>
    public event Action? TextModified;
    public event Action? InsertModeChanged;

    // ────────────────────────────────────────────────────────────────────
    //  Configuration
    // ────────────────────────────────────────────────────────────────────

    /// <summary>The document buffer.</summary>
    public PieceTable? Document
    {
        get => _document;
        set => _document = value;
    }

    /// <summary>The caret manager.</summary>
    public CaretManager? Caret
    {
        get => _caret;
        set => _caret = value;
    }

    /// <summary>The selection manager.</summary>
    public SelectionManager? Selection
    {
        get => _selection;
        set => _selection = value;
    }

    /// <summary>The undo/redo command history.</summary>
    public CommandHistory? History
    {
        get => _history;
        set => _history = value;
    }

    /// <summary>The folding manager.</summary>
    public FoldingManager? Folding
    {
        get => _folding;
        set => _folding = value;
    }

    /// <summary>Number of spaces per tab stop.</summary>
    public int TabSize
    {
        get => _tabSize;
        set => _tabSize = Math.Max(1, value);
    }

    /// <summary>
    /// <see langword="true"/> when in insert mode; <see langword="false"/>
    /// when in overwrite mode.
    /// </summary>
    public bool InsertMode => _insertMode;

    /// <summary>Whether the editor is in read-only mode.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Optional macro recorder that captures actions while recording.</summary>
    public MacroRecorder? MacroRecorder { get; set; }

    // ────────────────────────────────────────────────────────────────────
    //  Key processing
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Processes a key-down event. Returns <see langword="true"/> if the
    /// key was handled by this handler.
    /// </summary>
    public bool ProcessKeyDown(Keys key, bool ctrl, bool shift, bool alt)
    {
        if (_document is null || _caret is null || _selection is null) return false;

        Keys keyCode = key & Keys.KeyCode;

        // Record basic navigation for macro playback (unmodified keys only).
        if (!ctrl && !shift && !alt && MacroRecorder?.IsRecording == true)
        {
            switch (keyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    MacroRecorder.RecordAction(new MacroAction
                    {
                        ActionType = MacroActionType.MoveCaret,
                        Key = keyCode,
                    });
                    break;
            }
        }

        // ── Navigation (never blocked by ReadOnly) ──────────────────

        switch (keyCode)
        {
            case Keys.Left:
                if (ctrl && shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveWordLeft(); _selection.ExtendSelection(_caret.Offset); }
                else if (ctrl) { _selection.ClearSelection(); _caret.MoveWordLeft(); }
                else if (shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveLeft(); _selection.ExtendSelection(_caret.Offset); }
                else { if (_selection.HasSelection) { _caret.MoveTo(_selection.SelectionStart); _selection.ClearSelection(); } else _caret.MoveLeft(); }
                return true;

            case Keys.Right:
                if (ctrl && shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveWordRight(); _selection.ExtendSelection(_caret.Offset); }
                else if (ctrl) { _selection.ClearSelection(); _caret.MoveWordRight(); }
                else if (shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveRight(); _selection.ExtendSelection(_caret.Offset); }
                else { if (_selection.HasSelection) { _caret.MoveTo(_selection.SelectionEnd); _selection.ClearSelection(); } else _caret.MoveRight(); }
                return true;

            case Keys.Up:
                if (alt && !ReadOnly) { MoveLineUp(); return true; }
                if (ctrl && shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveUp(); _selection.ExtendSelection(_caret.Offset); }
                else if (shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveUp(); _selection.ExtendSelection(_caret.Offset); }
                else { _selection.ClearSelection(); _caret.MoveUp(); }
                return true;

            case Keys.Down:
                if (alt && !ReadOnly) { MoveLineDown(); return true; }
                if (ctrl && shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveDown(); _selection.ExtendSelection(_caret.Offset); }
                else if (shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveDown(); _selection.ExtendSelection(_caret.Offset); }
                else { _selection.ClearSelection(); _caret.MoveDown(); }
                return true;

            case Keys.Home:
                if (ctrl && shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveToDocumentStart(); _selection.ExtendSelection(_caret.Offset); }
                else if (ctrl) { _selection.ClearSelection(); _caret.MoveToDocumentStart(); }
                else if (shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveHome(); _selection.ExtendSelection(_caret.Offset); }
                else { _selection.ClearSelection(); _caret.MoveHome(); }
                return true;

            case Keys.End:
                if (ctrl && shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveToDocumentEnd(); _selection.ExtendSelection(_caret.Offset); }
                else if (ctrl) { _selection.ClearSelection(); _caret.MoveToDocumentEnd(); }
                else if (shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MoveEnd(); _selection.ExtendSelection(_caret.Offset); }
                else { _selection.ClearSelection(); _caret.MoveEnd(); }
                return true;

            case Keys.PageUp:
                if (shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MovePageUp(20); _selection.ExtendSelection(_caret.Offset); }
                else { _selection.ClearSelection(); _caret.MovePageUp(20); }
                return true;

            case Keys.PageDown:
                if (shift) { if (!_selection.HasSelection) _selection.StartSelection(_caret.Offset); _caret.MovePageDown(20); _selection.ExtendSelection(_caret.Offset); }
                else { _selection.ClearSelection(); _caret.MovePageDown(20); }
                return true;
        }

        // ── Ctrl shortcuts ──────────────────────────────────────────

        if (ctrl && !alt)
        {
            switch (keyCode)
            {
                case Keys.A:
                    _selection.SelectAll();
                    if (_document.Length > 0)
                        _caret.MoveTo(_document.Length);
                    return true;

                case Keys.C:
                    CopyToClipboard();
                    return true;

                case Keys.X:
                    if (!ReadOnly) CutToClipboard();
                    return true;

                case Keys.V:
                    if (!ReadOnly) PasteFromClipboard();
                    return true;

                case Keys.Z:
                    if (!ReadOnly) Undo();
                    return true;

                case Keys.Y:
                    if (!ReadOnly) Redo();
                    return true;

                case Keys.D:
                    if (!ReadOnly) DuplicateLine();
                    return true;

                case Keys.L:
                    if (!ReadOnly) DeleteLine();
                    return true;
            }

            if (shift)
            {
                switch (keyCode)
                {
                    case Keys.K:
                        if (!ReadOnly) DeleteLine();
                        return true;

                    case Keys.Up:
                        if (!ReadOnly) CopyLineUp();
                        return true;

                    case Keys.Down:
                        if (!ReadOnly) CopyLineDown();
                        return true;

                    case Keys.OemOpenBrackets: // Ctrl+Shift+[  → Fold current region
                        if (_folding is not null && _caret is not null)
                        {
                            var region = _folding.GetFoldRegionContaining(_caret.Line);
                            if (region.HasValue)
                                _folding.Collapse(region.Value.StartLine);
                        }
                        return true;

                    case Keys.OemCloseBrackets: // Ctrl+Shift+]  → Unfold current region
                        if (_folding is not null && _caret is not null)
                        {
                            var region2 = _folding.GetFoldRegionContaining(_caret.Line);
                            if (region2.HasValue)
                                _folding.Expand(region2.Value.StartLine);
                        }
                        return true;

                    case Keys.OemMinus: // Ctrl+Shift+-  → Collapse All
                        _folding?.CollapseAll();
                        return true;

                    case Keys.Oemplus: // Ctrl+Shift+=  → Expand All
                        _folding?.ExpandAll();
                        return true;
                }
            }
        }

        // ── Editing keys (blocked by ReadOnly) ─────────────────────

        if (ReadOnly) return false;

        switch (keyCode)
        {
            case Keys.Back:
                HandleBackspace();
                return true;

            case Keys.Delete:
                HandleDelete();
                return true;

            case Keys.Enter:
                HandleEnter();
                return true;

            case Keys.Tab:
                if (shift)
                    HandleUnindent();
                else
                    HandleTab();
                return true;

            case Keys.Insert:
                _insertMode = !_insertMode;
                InsertModeChanged?.Invoke();
                return true;
        }

        return false;
    }

    /// <summary>
    /// Processes a character input (printable character).
    /// </summary>
    public void ProcessCharInput(char c)
    {
        if (ReadOnly || _document is null || _caret is null || _selection is null || _history is null)
            return;

        if (c < 32 && c != '\t') return; // Ignore control characters.

        // Column mode: insert text on every line in the column selection.
        if (_selection.IsColumnMode)
        {
            ColumnInsertText(c.ToString());
            return;
        }

        DeleteSelectionIfAny();

        string text = c.ToString();

        if (!_insertMode && _caret.Offset < _document.Length)
        {
            // Overwrite mode: replace the character under the caret.
            var cmd = new ReplaceCommand(_document, _caret.Offset, 1, text);
            _history.Execute(cmd);
        }
        else
        {
            var cmd = new InsertCommand(_document, _caret.Offset, text);
            _history.Execute(cmd);
        }

        _caret.MoveTo(_caret.Offset + 1);
        MacroRecorder?.RecordAction(new MacroAction
        {
            ActionType = MacroActionType.TypeText,
            Text = text,
        });
        TextModified?.Invoke();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Editing operations
    // ────────────────────────────────────────────────────────────────────

    private void HandleBackspace()
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;

        if (_selection.IsColumnMode)
        {
            if (_selection.ColumnLeftCol != _selection.ColumnRightCol)
                DeleteColumnSelection();
            else
                ColumnBackspace();
            return;
        }

        if (_selection.HasSelection)
        {
            DeleteSelectionIfAny();
            TextModified?.Invoke();
            return;
        }

        if (_caret.Offset == 0) return;

        long deletePos = _caret.Offset - 1;
        var cmd = new DeleteCommand(_document, deletePos, 1);
        _history.Execute(cmd);
        _caret.MoveTo(deletePos);
        MacroRecorder?.RecordAction(new MacroAction { ActionType = MacroActionType.Backspace });
        TextModified?.Invoke();
    }

    private void HandleDelete()
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;

        if (_selection.IsColumnMode)
        {
            if (_selection.ColumnLeftCol != _selection.ColumnRightCol)
                DeleteColumnSelection();
            else
                ColumnDeleteForward();
            return;
        }

        if (_selection.HasSelection)
        {
            DeleteSelectionIfAny();
            TextModified?.Invoke();
            return;
        }

        if (_caret.Offset >= _document.Length) return;

        var cmd = new DeleteCommand(_document, _caret.Offset, 1);
        _history.Execute(cmd);
        MacroRecorder?.RecordAction(new MacroAction { ActionType = MacroActionType.Delete });
        TextModified?.Invoke();
    }

    private void HandleEnter()
    {
        if (_document is null || _caret is null || _history is null) return;

        // Exit column mode on Enter.
        if (_selection is not null && _selection.IsColumnMode)
            _selection.ClearSelection();

        DeleteSelectionIfAny();

        // Compute auto-indent: copy leading whitespace from current line.
        string indent = "";
        if (EditorControl.DefaultAutoIndent)
        {
            string currentLineText = _document.GetLine(_caret.Line);
            foreach (char c in currentLineText)
            {
                if (c == ' ' || c == '\t')
                    indent += c;
                else
                    break;
            }
        }

        string newLine = "\n" + indent;
        var cmd = new InsertCommand(_document, _caret.Offset, newLine);
        _history.Execute(cmd);
        _caret.MoveTo(_caret.Offset + newLine.Length);
        MacroRecorder?.RecordAction(new MacroAction
        {
            ActionType = MacroActionType.TypeText,
            Text = newLine,
        });
        TextModified?.Invoke();
    }

    private void HandleTab()
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;

        if (_selection.HasSelection)
        {
            // Indent selected lines.
            IndentSelectedLines();
            return;
        }

        string tabText = new string(' ', _tabSize);
        DeleteSelectionIfAny();

        var cmd = new InsertCommand(_document, _caret.Offset, tabText);
        _history.Execute(cmd);
        _caret.MoveTo(_caret.Offset + tabText.Length);
        MacroRecorder?.RecordAction(new MacroAction
        {
            ActionType = MacroActionType.TypeText,
            Text = tabText,
        });
        TextModified?.Invoke();
    }

    private void HandleUnindent()
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;

        if (_selection.HasSelection)
        {
            UnindentSelectedLines();
            return;
        }

        // Remove up to TabSize spaces from the beginning of the current line.
        string lineText = _document.GetLine(_caret.Line);
        int spacesToRemove = 0;
        for (int i = 0; i < Math.Min(_tabSize, lineText.Length); i++)
        {
            if (lineText[i] == ' ') spacesToRemove++;
            else if (lineText[i] == '\t') { spacesToRemove++; break; }
            else break;
        }

        if (spacesToRemove == 0) return;

        long lineStart = _document.GetLineStartOffset(_caret.Line);
        var cmd = new DeleteCommand(_document, lineStart, spacesToRemove);
        _history.Execute(cmd);

        _caret.MoveTo(Math.Max(lineStart, _caret.Offset - spacesToRemove));
        TextModified?.Invoke();
    }

    private void IndentSelectedLines()
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;
        if (!_selection.HasSelection) return;

        // Determine the range of lines.
        var (startLine, _) = GetOffsetLineColumn(_selection.SelectionStart);
        var (endLine, _) = GetOffsetLineColumn(_selection.SelectionEnd - 1);

        string indent = new string(' ', _tabSize);
        var commands = new List<ICommand>();

        // Process from bottom to top to keep offsets valid.
        for (long line = endLine; line >= startLine; line--)
        {
            long lineStart = _document.GetLineStartOffset(line);
            commands.Add(new InsertCommand(_document, lineStart, indent));
        }

        var composite = new CompositeCommand("Indent lines", commands);
        _history.Execute(composite);
        TextModified?.Invoke();
    }

    private void UnindentSelectedLines()
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;
        if (!_selection.HasSelection) return;

        var (startLine, _) = GetOffsetLineColumn(_selection.SelectionStart);
        var (endLine, _) = GetOffsetLineColumn(_selection.SelectionEnd - 1);

        var commands = new List<ICommand>();

        for (long line = endLine; line >= startLine; line--)
        {
            string lineText = _document.GetLine(line);
            int spacesToRemove = 0;
            for (int i = 0; i < Math.Min(_tabSize, lineText.Length); i++)
            {
                if (lineText[i] == ' ') spacesToRemove++;
                else if (lineText[i] == '\t') { spacesToRemove++; break; }
                else break;
            }

            if (spacesToRemove > 0)
            {
                long lineStart = _document.GetLineStartOffset(line);
                commands.Add(new DeleteCommand(_document, lineStart, spacesToRemove));
            }
        }

        if (commands.Count > 0)
        {
            var composite = new CompositeCommand("Unindent lines", commands);
            _history.Execute(composite);
            TextModified?.Invoke();
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Clipboard operations
    // ────────────────────────────────────────────────────────────────────

    private void CopyToClipboard()
    {
        if (_selection is null) return;

        // Column mode copy.
        if (_selection.IsColumnMode && _selection.HasColumnSelection && _document is not null)
        {
            string text = _selection.GetColumnSelectedText(_document, _tabSize);
            if (!string.IsNullOrEmpty(text))
            {
                var dataObj = new DataObject();
                dataObj.SetText(text);
                dataObj.SetData("BascankaCursorColumnSelect", true);
                Clipboard.SetDataObject(dataObj);
            }
            return;
        }

        if (!_selection.HasSelection) return;

        string selText = _selection.GetSelectedText();
        if (!string.IsNullOrEmpty(selText))
        {
            Clipboard.SetText(selText);
        }
    }

    private void CutToClipboard()
    {
        if (_selection is null || _document is null || _caret is null || _history is null) return;

        // Column mode cut.
        if (_selection.IsColumnMode && _selection.HasColumnSelection)
        {
            CopyToClipboard();
            DeleteColumnSelection();
            return;
        }

        if (!_selection.HasSelection) return;

        CopyToClipboard();
        DeleteSelectionIfAny();
        TextModified?.Invoke();
    }

    private void PasteFromClipboard()
    {
        if (_document is null || _caret is null || _history is null) return;

        // Check for column paste.
        var dataObj = Clipboard.GetDataObject();
        bool isColumnPaste = dataObj?.GetDataPresent("BascankaCursorColumnSelect") == true;
        string? text = dataObj?.GetData(DataFormats.UnicodeText) as string
            ?? dataObj?.GetData(DataFormats.Text) as string;

        if (string.IsNullOrEmpty(text)) return;

        if (isColumnPaste)
        {
            // Delete column selection first if active.
            if (_selection is not null && _selection.IsColumnMode && _selection.HasColumnSelection)
                DeleteColumnSelection();
            else
                DeleteSelectionIfAny();

            ColumnPaste(text);
            return;
        }

        DeleteSelectionIfAny();

        var cmd = new InsertCommand(_document, _caret.Offset, text);
        _history.Execute(cmd);
        _caret.MoveTo(_caret.Offset + text.Length);
        TextModified?.Invoke();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Undo / Redo
    // ────────────────────────────────────────────────────────────────────

    private void Undo()
    {
        if (_history is null || !_history.CanUndo) return;
        _history.Undo();
        TextModified?.Invoke();
    }

    private void Redo()
    {
        if (_history is null || !_history.CanRedo) return;
        _history.Redo();
        TextModified?.Invoke();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Line operations
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Duplicates the current line (Ctrl+D).</summary>
    private void DuplicateLine()
    {
        if (_document is null || _caret is null || _history is null) return;

        long line = _caret.Line;
        string lineText = _document.GetLine(line);

        long lineStart = _document.GetLineStartOffset(line);
        long insertOffset;
        string textToInsert;

        if (line + 1 < _document.LineCount)
        {
            insertOffset = _document.GetLineStartOffset(line + 1);
            textToInsert = lineText + "\n";
        }
        else
        {
            insertOffset = _document.Length;
            textToInsert = "\n" + lineText;
        }

        var cmd = new InsertCommand(_document, insertOffset, textToInsert);
        _history.Execute(cmd);
        _caret.MoveToLineColumn(line + 1, _caret.Column);
        TextModified?.Invoke();
    }

    /// <summary>Deletes the current line (Ctrl+L or Ctrl+Shift+K).</summary>
    private void DeleteLine()
    {
        if (_document is null || _caret is null || _history is null) return;

        long line = _caret.Line;
        long lineStart = _document.GetLineStartOffset(line);
        long deleteLength;

        if (line + 1 < _document.LineCount)
        {
            deleteLength = _document.GetLineStartOffset(line + 1) - lineStart;
        }
        else if (line > 0)
        {
            // Last line: also delete the preceding newline.
            long prevLineEnd = _document.GetLineStartOffset(line);
            lineStart = prevLineEnd - 1; // include the \n before this line
            deleteLength = _document.Length - lineStart;
        }
        else
        {
            deleteLength = _document.Length;
        }

        if (deleteLength <= 0) return;

        var cmd = new DeleteCommand(_document, lineStart, deleteLength);
        _history.Execute(cmd);

        long newLine = Math.Min(line, _document.LineCount - 1);
        _caret.MoveToLineColumn(newLine, 0);
        _selection?.ClearSelection();
        TextModified?.Invoke();
    }

    /// <summary>Moves the current line up (Alt+Up).</summary>
    private void MoveLineUp()
    {
        if (_document is null || _caret is null || _history is null) return;
        if (_caret.Line == 0) return;

        long line = _caret.Line;
        string currentLineText = _document.GetLine(line);
        string aboveLineText = _document.GetLine(line - 1);

        // Delete current line and the line above, then reinsert in swapped order.
        long aboveStart = _document.GetLineStartOffset(line - 1);
        long currentEnd;
        if (line + 1 < _document.LineCount)
            currentEnd = _document.GetLineStartOffset(line + 1);
        else
            currentEnd = _document.Length;

        long totalLen = currentEnd - aboveStart;
        string replacement;
        if (line + 1 < _document.LineCount)
            replacement = currentLineText + "\n" + aboveLineText + "\n";
        else
            replacement = currentLineText + "\n" + aboveLineText;

        var cmd = new ReplaceCommand(_document, aboveStart, totalLen, replacement);
        _history.Execute(cmd);

        _caret.MoveToLineColumn(line - 1, _caret.Column);
        TextModified?.Invoke();
    }

    /// <summary>Moves the current line down (Alt+Down).</summary>
    private void MoveLineDown()
    {
        if (_document is null || _caret is null || _history is null) return;
        if (_caret.Line >= _document.LineCount - 1) return;

        long line = _caret.Line;
        string currentLineText = _document.GetLine(line);
        string belowLineText = _document.GetLine(line + 1);

        long currentStart = _document.GetLineStartOffset(line);
        long belowEnd;
        if (line + 2 < _document.LineCount)
            belowEnd = _document.GetLineStartOffset(line + 2);
        else
            belowEnd = _document.Length;

        long totalLen = belowEnd - currentStart;
        string replacement;
        if (line + 2 < _document.LineCount)
            replacement = belowLineText + "\n" + currentLineText + "\n";
        else
            replacement = belowLineText + "\n" + currentLineText;

        var cmd = new ReplaceCommand(_document, currentStart, totalLen, replacement);
        _history.Execute(cmd);

        _caret.MoveToLineColumn(line + 1, _caret.Column);
        TextModified?.Invoke();
    }

    /// <summary>Copies the current line up (Ctrl+Shift+Up).</summary>
    private void CopyLineUp()
    {
        if (_document is null || _caret is null || _history is null) return;

        long line = _caret.Line;
        string lineText = _document.GetLine(line);
        long lineStart = _document.GetLineStartOffset(line);

        var cmd = new InsertCommand(_document, lineStart, lineText + "\n");
        _history.Execute(cmd);

        // Caret stays on the same line number (which is now the copy above).
        TextModified?.Invoke();
    }

    /// <summary>Copies the current line down (Ctrl+Shift+Down).</summary>
    private void CopyLineDown()
    {
        if (_document is null || _caret is null || _history is null) return;

        long line = _caret.Line;
        string lineText = _document.GetLine(line);

        long insertOffset;
        string textToInsert;
        if (line + 1 < _document.LineCount)
        {
            insertOffset = _document.GetLineStartOffset(line + 1);
            textToInsert = lineText + "\n";
        }
        else
        {
            insertOffset = _document.Length;
            textToInsert = "\n" + lineText;
        }

        var cmd = new InsertCommand(_document, insertOffset, textToInsert);
        _history.Execute(cmd);

        _caret.MoveToLineColumn(line + 1, _caret.Column);
        TextModified?.Invoke();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Column (box) selection operations
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts text on every line of the column selection, replacing the
    /// selected column range if it has width. All edits are grouped in a
    /// single <see cref="CompositeCommand"/> for atomic undo.
    /// </summary>
    private void ColumnInsertText(string text)
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;
        if (!_selection.IsColumnMode) return;

        long startLine = _selection.ColumnStartLine;
        long endLine = _selection.ColumnEndLine;
        int leftExpCol = (int)_selection.ColumnLeftCol;   // visual column
        int rightExpCol = (int)_selection.ColumnRightCol;  // visual column
        bool hasWidth = leftExpCol != rightExpCol;

        var commands = new List<ICommand>();

        // Process from bottom to top to keep earlier offsets valid.
        for (long line = endLine; line >= startLine; line--)
        {
            if (line >= _document.LineCount) continue;

            string lineText = _document.GetLine(line);
            long lineStart = _document.GetLineStartOffset(line);

            // Convert expanded (visual) columns to character indices for this line.
            int charLeft = SelectionManager.CompressedColumnAt(lineText, leftExpCol, _tabSize);
            int charRight = SelectionManager.CompressedColumnAt(lineText, rightExpCol, _tabSize);

            // If the line is shorter than the insert column, pad with spaces.
            if (charLeft >= lineText.Length && leftExpCol > 0)
            {
                int paddingNeeded = leftExpCol - ExpandedColumnAt(lineText, lineText.Length);
                if (paddingNeeded > 0)
                {
                    string padding = new string(' ', paddingNeeded);
                    commands.Add(new InsertCommand(_document, lineStart + lineText.Length, padding));
                }
                // After padding, insert at the padded position.
                commands.Add(new InsertCommand(_document, lineStart + lineText.Length + Math.Max(0, leftExpCol - ExpandedColumnAt(lineText, lineText.Length)), text));
            }
            else if (hasWidth && charRight > charLeft)
            {
                // Delete the column range, then insert.
                commands.Add(new DeleteCommand(_document, lineStart + charLeft, charRight - charLeft));
                commands.Add(new InsertCommand(_document, lineStart + charLeft, text));
            }
            else
            {
                // Zero-width selection (column cursor): just insert.
                commands.Add(new InsertCommand(_document, lineStart + charLeft, text));
            }
        }

        if (commands.Count > 0)
        {
            var composite = new CompositeCommand("Column insert", commands);
            _history.Execute(composite);
        }

        // Advance the column cursor so subsequent typing continues in column mode.
        int newExpCol = leftExpCol + text.Length;
        _selection.StartColumnSelection(startLine, newExpCol);
        _selection.ExtendColumnSelection(endLine, newExpCol);

        // Move caret to the character position on the first line.
        string newLineText = _document.GetLine(startLine);
        int caretCharCol = SelectionManager.CompressedColumnAt(newLineText, newExpCol, _tabSize);
        _caret.MoveToLineColumn(startLine, caretCharCol);
        TextModified?.Invoke();
    }

    /// <summary>
    /// Returns the expanded (visual) column for a character index in a line.
    /// </summary>
    private int ExpandedColumnAt(string lineText, int charIndex)
    {
        int col = 0;
        int limit = Math.Min(charIndex, lineText.Length);
        for (int i = 0; i < limit; i++)
        {
            if (lineText[i] == '\t')
                col += _tabSize - (col % _tabSize);
            else
                col++;
        }
        return col;
    }

    /// <summary>
    /// Deletes the column-selected rectangle. All edits are grouped in a
    /// single <see cref="CompositeCommand"/> for atomic undo.
    /// </summary>
    private void DeleteColumnSelection()
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;
        if (!_selection.IsColumnMode || !_selection.HasColumnSelection) return;

        long startLine = _selection.ColumnStartLine;
        long endLine = _selection.ColumnEndLine;
        int leftExpCol = (int)_selection.ColumnLeftCol;
        int rightExpCol = (int)_selection.ColumnRightCol;

        var commands = new List<ICommand>();

        // Process from bottom to top to keep earlier offsets valid.
        for (long line = endLine; line >= startLine; line--)
        {
            if (line >= _document.LineCount) continue;

            string lineText = _document.GetLine(line);
            long lineStart = _document.GetLineStartOffset(line);

            int charLeft = SelectionManager.CompressedColumnAt(lineText, leftExpCol, _tabSize);
            int charRight = SelectionManager.CompressedColumnAt(lineText, rightExpCol, _tabSize);

            if (charRight > charLeft)
            {
                commands.Add(new DeleteCommand(_document, lineStart + charLeft, charRight - charLeft));
            }
        }

        if (commands.Count > 0)
        {
            var composite = new CompositeCommand("Column delete", commands);
            _history.Execute(composite);
        }

        long caretCharCol = SelectionManager.CompressedColumnAt(
            _document.GetLine(startLine), leftExpCol, _tabSize);
        _caret.MoveToLineColumn(startLine, caretCharCol);

        // Stay in column mode with a zero-width selection at the left edge
        // so the user can continue typing/deleting in column mode.
        _selection.StartColumnSelection(startLine, leftExpCol);
        _selection.ExtendColumnSelection(endLine, leftExpCol);
        TextModified?.Invoke();
    }

    /// <summary>
    /// Deletes one character before the column cursor on each line
    /// (zero-width column backspace).
    /// </summary>
    private void ColumnBackspace()
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;
        if (!_selection.IsColumnMode) return;

        long startLine = _selection.ColumnStartLine;
        long endLine = _selection.ColumnEndLine;
        int expCol = (int)_selection.ColumnLeftCol; // same as RightCol for zero-width

        if (expCol == 0) return; // Nothing to delete before column 0.

        var commands = new List<ICommand>();

        for (long line = endLine; line >= startLine; line--)
        {
            if (line >= _document.LineCount) continue;

            string lineText = _document.GetLine(line);
            int charCol = SelectionManager.CompressedColumnAt(lineText, expCol, _tabSize);

            if (charCol > 0 && charCol <= lineText.Length)
            {
                commands.Add(new DeleteCommand(_document, _document.GetLineStartOffset(line) + charCol - 1, 1));
            }
        }

        if (commands.Count > 0)
        {
            var composite = new CompositeCommand("Column backspace", commands);
            _history.Execute(composite);
        }

        // Move the column cursor one position to the left.
        int newExpCol = expCol - 1;
        _selection.StartColumnSelection(startLine, newExpCol);
        _selection.ExtendColumnSelection(endLine, newExpCol);

        string newLineText = _document.GetLine(startLine);
        int caretCharCol = SelectionManager.CompressedColumnAt(newLineText, newExpCol, _tabSize);
        _caret.MoveToLineColumn(startLine, caretCharCol);
        TextModified?.Invoke();
    }

    /// <summary>
    /// Deletes one character after the column cursor on each line
    /// (zero-width column delete).
    /// </summary>
    private void ColumnDeleteForward()
    {
        if (_document is null || _caret is null || _selection is null || _history is null) return;
        if (!_selection.IsColumnMode) return;

        long startLine = _selection.ColumnStartLine;
        long endLine = _selection.ColumnEndLine;
        int expCol = (int)_selection.ColumnLeftCol;

        var commands = new List<ICommand>();

        for (long line = endLine; line >= startLine; line--)
        {
            if (line >= _document.LineCount) continue;

            string lineText = _document.GetLine(line);
            int charCol = SelectionManager.CompressedColumnAt(lineText, expCol, _tabSize);

            if (charCol < lineText.Length)
            {
                commands.Add(new DeleteCommand(_document, _document.GetLineStartOffset(line) + charCol, 1));
            }
        }

        if (commands.Count > 0)
        {
            var composite = new CompositeCommand("Column delete forward", commands);
            _history.Execute(composite);
        }

        // Column cursor stays at the same position.
        _selection.StartColumnSelection(startLine, expCol);
        _selection.ExtendColumnSelection(endLine, expCol);

        string newLineText = _document.GetLine(startLine);
        int caretCharCol = SelectionManager.CompressedColumnAt(newLineText, expCol, _tabSize);
        _caret.MoveToLineColumn(startLine, caretCharCol);
        TextModified?.Invoke();
    }

    /// <summary>
    /// Pastes column-copied text, inserting each line at consecutive
    /// document lines starting from the caret position at the caret column.
    /// </summary>
    private void ColumnPaste(string text)
    {
        if (_document is null || _caret is null || _history is null) return;

        string[] lines = text.Split('\n');
        long startLine = _caret.Line;
        long caretCol = _caret.Column;

        // Get the expanded column of the caret for consistent paste position.
        string caretLineText = _document.GetLine(startLine);
        int expCol = ExpandedColumnAt(caretLineText, (int)Math.Min(caretCol, caretLineText.Length));

        var commands = new List<ICommand>();

        // Process from bottom to top.
        int lastIdx = Math.Min(lines.Length - 1, (int)(_document.LineCount - 1 - startLine));
        for (int i = lastIdx; i >= 0; i--)
        {
            long docLine = startLine + i;
            if (docLine >= _document.LineCount) continue;

            string lineText = _document.GetLine(docLine);
            long lineStart = _document.GetLineStartOffset(docLine);
            int charCol = SelectionManager.CompressedColumnAt(lineText, expCol, _tabSize);

            string pasteText = lines[i];
            if (string.IsNullOrEmpty(pasteText)) continue;

            // Pad with spaces if the line is shorter than the paste column.
            if (charCol >= lineText.Length)
            {
                int lineExpEnd = ExpandedColumnAt(lineText, lineText.Length);
                int paddingNeeded = expCol - lineExpEnd;
                if (paddingNeeded > 0)
                {
                    string padding = new string(' ', paddingNeeded);
                    commands.Add(new InsertCommand(_document, lineStart + lineText.Length, padding));
                }
                commands.Add(new InsertCommand(_document, lineStart + lineText.Length + Math.Max(0, paddingNeeded), pasteText));
            }
            else
            {
                commands.Add(new InsertCommand(_document, lineStart + charCol, pasteText));
            }
        }

        if (commands.Count > 0)
        {
            var composite = new CompositeCommand("Column paste", commands);
            _history.Execute(composite);
        }

        // Move caret past the pasted text on the first line.
        if (lines.Length > 0)
        {
            long newCol = caretCol + lines[0].Length;
            _caret.MoveToLineColumn(startLine, Math.Min(newCol, _document.GetLineLength(startLine)));
        }

        TextModified?.Invoke();
    }

    // ────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes selected text if there is an active selection.
    /// </summary>
    private void DeleteSelectionIfAny()
    {
        if (_selection is null || !_selection.HasSelection ||
            _document is null || _caret is null || _history is null) return;

        long start = _selection.SelectionStart;
        long length = _selection.SelectionEnd - start;

        var cmd = new DeleteCommand(_document, start, length);
        _history.Execute(cmd);

        _caret.MoveTo(start);
        _selection.ClearSelection();
    }

    private (long Line, long Column) GetOffsetLineColumn(long offset)
    {
        if (_document is null) return (0, 0);

        // Walk lines to find the line for the offset.
        long line = 0;
        long lineStart = 0;

        for (long i = 0; i < _document.LineCount; i++)
        {
            long nextLineStart;
            if (i + 1 < _document.LineCount)
                nextLineStart = _document.GetLineStartOffset(i + 1);
            else
                nextLineStart = _document.Length + 1;

            if (offset < nextLineStart)
            {
                line = i;
                lineStart = _document.GetLineStartOffset(i);
                break;
            }
        }

        return (line, offset - lineStart);
    }
}
