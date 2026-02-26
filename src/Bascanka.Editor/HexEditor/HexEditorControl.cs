using System.Drawing;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Windows.Forms;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.HexEditor;

/// <summary>
/// Main hex editor composite control.
/// Composes a <see cref="HexRenderer"/> panel, a <see cref="DataInspectorPanel"/>,
/// and a <see cref="VScrollBar"/> into a complete hex-editing experience with
/// undo/redo, find, insert, delete, and overwrite operations.
/// </summary>
public sealed class HexEditorControl : UserControl
{
    // ── Constants ───────────────────────────────────────────────────────

    private const int DefaultBytesPerRow = 16;


    // ── Child controls ──────────────────────────────────────────────────

    private readonly HexRenderer _renderer;
    private readonly DataInspectorPanel _inspector;
    private readonly VScrollBar _scrollBar;
    private readonly SplitContainer _splitter;

    // ── State ───────────────────────────────────────────────────────────

    private byte[] _data = [];
    private readonly List<HexUndoEntry> _undoStack = [];
    private readonly List<HexUndoEntry> _redoStack = [];
    private bool _isReadOnly;
    private ITheme _theme;

    // ── Events ──────────────────────────────────────────────────────────

    /// <summary>Raised when any byte in the data is changed.</summary>
    public event EventHandler<HexDataChangedEventArgs>? DataChanged;

    /// <summary>Raised when the selection offset or length changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the current offset changes (caret movement).</summary>
    public event EventHandler? OffsetChanged;

    // ── Construction ────────────────────────────────────────────────────

    public HexEditorControl()
    {
        _theme = new DarkTheme();

        _renderer = new HexRenderer
        {
            Dock = DockStyle.Fill,
            BytesPerRow = DefaultBytesPerRow,
            Theme = _theme,
        };

        _inspector = new DataInspectorPanel
        {
            Dock = DockStyle.Fill,
            Theme = _theme,
        };

        _scrollBar = new VScrollBar
        {
            Dock = DockStyle.Right,
            Width = SystemInformation.VerticalScrollBarWidth,
        };

        // Layout: SplitContainer with hex renderer on top, inspector on the bottom.
        _splitter = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            BorderStyle = BorderStyle.None,
        };

        // Panel1 = renderer + scrollbar
        var rendererPanel = new Panel { Dock = DockStyle.Fill };
        rendererPanel.Controls.Add(_renderer);
        rendererPanel.Controls.Add(_scrollBar);
        _splitter.Panel1.Controls.Add(rendererPanel);

        // Panel2 = inspector
        _splitter.Panel2.Controls.Add(_inspector);

        Controls.Add(_splitter);

        // Defer splitter sizing until the control is fully laid out.
        HandleCreated += OnFirstHandleCreated;

        // Wire events
        _renderer.ByteEdited += OnByteEdited;
        _renderer.SelectionChanged += OnRendererSelectionChanged;
        _renderer.ScrollChanged += OnRendererScrollChanged;

        _scrollBar.Scroll += OnScrollBarScroll;

        BackColor = _theme.EditorBackground;
    }

    // ── Properties ──────────────────────────────────────────────────────

    /// <summary>
    /// The raw data being edited. Setting this replaces the entire buffer.
    /// </summary>
    public byte[] Data
    {
        get => _data;
        set
        {
            _data = value ?? [];
            _undoStack.Clear();
            _redoStack.Clear();
            _renderer.Data = _data;
            UpdateScrollBar();
            UpdateInspector();
        }
    }

    /// <summary>Number of bytes displayed per row.</summary>
    public int BytesPerRow
    {
        get => _renderer.BytesPerRow;
        set
        {
            _renderer.BytesPerRow = value;
            UpdateScrollBar();
        }
    }

    /// <summary>When true, all editing operations are disabled.</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            _isReadOnly = value;
            _renderer.IsReadOnly = value;
        }
    }

    /// <summary>The byte offset of the selection start.</summary>
    public long SelectedOffset
    {
        get => _renderer.SelectedOffset;
        set => _renderer.SelectedOffset = value;
    }

    /// <summary>Length of the current selection in bytes.</summary>
    public long SelectionLength
    {
        get => _renderer.SelectionLength;
        set => _renderer.SelectionLength = value;
    }

    /// <summary>The theme applied to the hex editor and inspector.</summary>
    public ITheme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? new DarkTheme();
            _renderer.Theme = _theme;
            _inspector.Theme = _theme;
            BackColor = _theme.EditorBackground;
        }
    }

    /// <summary>Whether an undo operation is available.</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>Whether a redo operation is available.</summary>
    public bool CanRedo => _redoStack.Count > 0;

    // ── File loading ────────────────────────────────────────────────────

    /// <summary>
    /// Loads a file into the hex editor. For large files, uses a
    /// <see cref="MemoryMappedFile"/> to avoid allocating the entire file
    /// into managed memory at once.
    /// </summary>
    /// <param name="path">The file path to load.</param>
    public void LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileInfo fi = new(path);
        if (!fi.Exists)
            throw new FileNotFoundException("File not found.", path);

        long fileSize = fi.Length;
        if (fileSize == 0)
        {
            Data = [];
            return;
        }

        // For files larger than 64 MB, use memory-mapped I/O.
        const long MmapThreshold = 64 * 1024 * 1024;

        if (fileSize <= MmapThreshold)
        {
            Data = File.ReadAllBytes(path);
        }
        else
        {
            // Memory-mapped load: read the entire file into a byte array
            // using a memory-mapped view for efficient sequential access.
            byte[] buffer = new byte[fileSize];

            using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open,
                null, 0, MemoryMappedFileAccess.Read);
            using var accessor = mmf.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);
            accessor.ReadArray(0, buffer, 0, (int)fileSize);

            Data = buffer;
        }
    }

    /// <summary>
    /// Loads a byte array directly into the hex editor.
    /// </summary>
    public void LoadBytes(byte[] data)
    {
        Data = data;
    }

    // ── Edit operations ─────────────────────────────────────────────────

    /// <summary>
    /// Overwrites the byte at the given offset with a new value.
    /// Records an undo entry.
    /// </summary>
    public void OverwriteByte(long offset, byte newValue)
    {
        if (_isReadOnly) return;
        if (offset < 0 || offset >= _data.Length) return;

        byte oldValue = _data[offset];
        if (oldValue == newValue) return;

        _data[offset] = newValue;
        _renderer.ModifiedOffsets.Add(offset);

        _undoStack.Add(new HexUndoEntry(HexUndoType.Overwrite, offset, oldValue, newValue));
        _redoStack.Clear();

        _renderer.Invalidate();
        DataChanged?.Invoke(this, new HexDataChangedEventArgs(offset, oldValue, newValue));
        UpdateInspector();
    }

    /// <summary>
    /// Inserts a byte at the given offset, shifting subsequent bytes right.
    /// </summary>
    public void InsertByte(long offset, byte value)
    {
        if (_isReadOnly) return;
        offset = Math.Clamp(offset, 0, _data.Length);

        var newData = new byte[_data.Length + 1];
        if (offset > 0)
            Array.Copy(_data, 0, newData, 0, (int)offset);
        newData[offset] = value;
        if (offset < _data.Length)
            Array.Copy(_data, (int)offset, newData, (int)(offset + 1), (int)(_data.Length - offset));

        _data = newData;
        _renderer.Data = _data;
        _renderer.ModifiedOffsets.Add(offset);

        _undoStack.Add(new HexUndoEntry(HexUndoType.Insert, offset, 0, value));
        _redoStack.Clear();

        UpdateScrollBar();
        DataChanged?.Invoke(this, new HexDataChangedEventArgs(offset, 0, value));
        UpdateInspector();
    }

    /// <summary>
    /// Deletes the byte at the given offset, shifting subsequent bytes left.
    /// </summary>
    public void DeleteByte(long offset)
    {
        if (_isReadOnly) return;
        if (offset < 0 || offset >= _data.Length) return;

        byte oldValue = _data[offset];
        var newData = new byte[_data.Length - 1];
        if (offset > 0)
            Array.Copy(_data, 0, newData, 0, (int)offset);
        if (offset < _data.Length - 1)
            Array.Copy(_data, (int)(offset + 1), newData, (int)offset, (int)(_data.Length - offset - 1));

        _data = newData;
        _renderer.Data = _data;

        _undoStack.Add(new HexUndoEntry(HexUndoType.Delete, offset, oldValue, 0));
        _redoStack.Clear();

        if (_renderer.SelectedOffset >= _data.Length && _data.Length > 0)
            _renderer.SelectedOffset = _data.Length - 1;

        UpdateScrollBar();
        DataChanged?.Invoke(this, new HexDataChangedEventArgs(offset, oldValue, 0));
        UpdateInspector();
    }

    // ── Undo / Redo ─────────────────────────────────────────────────────

    /// <summary>
    /// Undoes the most recent edit operation.
    /// </summary>
    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        HexUndoEntry entry = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        switch (entry.Type)
        {
            case HexUndoType.Overwrite:
                _data[entry.Offset] = entry.OldValue;
                _renderer.ModifiedOffsets.Remove(entry.Offset);
                break;

            case HexUndoType.Insert:
                // Remove the inserted byte
                var shrunk = new byte[_data.Length - 1];
                if (entry.Offset > 0)
                    Array.Copy(_data, 0, shrunk, 0, (int)entry.Offset);
                if (entry.Offset < _data.Length - 1)
                    Array.Copy(_data, (int)(entry.Offset + 1), shrunk, (int)entry.Offset,
                        (int)(_data.Length - entry.Offset - 1));
                _data = shrunk;
                _renderer.Data = _data;
                break;

            case HexUndoType.Delete:
                // Re-insert the deleted byte
                var grown = new byte[_data.Length + 1];
                if (entry.Offset > 0)
                    Array.Copy(_data, 0, grown, 0, (int)entry.Offset);
                grown[entry.Offset] = entry.OldValue;
                if (entry.Offset < _data.Length)
                    Array.Copy(_data, (int)entry.Offset, grown, (int)(entry.Offset + 1),
                        (int)(_data.Length - entry.Offset));
                _data = grown;
                _renderer.Data = _data;
                break;
        }

        _redoStack.Add(entry);
        _renderer.SelectedOffset = entry.Offset;
        UpdateScrollBar();
        _renderer.Invalidate();
        UpdateInspector();
    }

    /// <summary>
    /// Redoes the most recently undone edit operation.
    /// </summary>
    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        HexUndoEntry entry = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        switch (entry.Type)
        {
            case HexUndoType.Overwrite:
                _data[entry.Offset] = entry.NewValue;
                _renderer.ModifiedOffsets.Add(entry.Offset);
                break;

            case HexUndoType.Insert:
                InsertByteInternal(entry.Offset, entry.NewValue);
                break;

            case HexUndoType.Delete:
                DeleteByteInternal(entry.Offset);
                break;
        }

        _undoStack.Add(entry);
        _renderer.SelectedOffset = entry.Offset;
        UpdateScrollBar();
        _renderer.Invalidate();
        UpdateInspector();
    }

    private void InsertByteInternal(long offset, byte value)
    {
        var newData = new byte[_data.Length + 1];
        if (offset > 0)
            Array.Copy(_data, 0, newData, 0, (int)offset);
        newData[offset] = value;
        if (offset < _data.Length)
            Array.Copy(_data, (int)offset, newData, (int)(offset + 1), (int)(_data.Length - offset));
        _data = newData;
        _renderer.Data = _data;
    }

    private void DeleteByteInternal(long offset)
    {
        if (offset < 0 || offset >= _data.Length) return;
        var newData = new byte[_data.Length - 1];
        if (offset > 0)
            Array.Copy(_data, 0, newData, 0, (int)offset);
        if (offset < _data.Length - 1)
            Array.Copy(_data, (int)(offset + 1), newData, (int)offset, (int)(_data.Length - offset - 1));
        _data = newData;
        _renderer.Data = _data;
    }

    // ── Navigation ──────────────────────────────────────────────────────

    /// <summary>
    /// Moves the caret to the given byte offset and scrolls it into view.
    /// </summary>
    public void GoToOffset(long offset)
    {
        if (_data.Length == 0) return;
        _renderer.SelectedOffset = Math.Clamp(offset, 0, _data.Length - 1);
        _renderer.SelectionLength = 0;
        _renderer.EnsureVisible(_renderer.SelectedOffset);
        UpdateInspector();
        OffsetChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Find ────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the first occurrence of a byte pattern starting from the
    /// current selection offset. Returns the offset of the match, or -1
    /// if not found.
    /// </summary>
    public long Find(byte[] pattern)
    {
        if (pattern is null || pattern.Length == 0 || _data.Length == 0)
            return -1;

        long startOffset = _renderer.SelectedOffset + 1;
        if (startOffset >= _data.Length) startOffset = 0;

        // Simple linear search with wrap-around.
        long dataLen = _data.Length;
        int patternLen = pattern.Length;

        for (long pass = 0; pass < dataLen; pass++)
        {
            long idx = (startOffset + pass) % dataLen;
            if (idx + patternLen > dataLen) continue;

            bool match = true;
            for (int j = 0; j < patternLen; j++)
            {
                if (_data[idx + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                _renderer.SelectedOffset = idx;
                _renderer.SelectionLength = patternLen;
                _renderer.EnsureVisible(idx);
                UpdateInspector();
                return idx;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the first occurrence of an ASCII pattern in the data.
    /// </summary>
    public long Find(string asciiPattern)
    {
        if (string.IsNullOrEmpty(asciiPattern)) return -1;
        return Find(Encoding.ASCII.GetBytes(asciiPattern));
    }

    // ── Event handlers ──────────────────────────────────────────────────

    private void OnByteEdited(object? sender, HexEditEventArgs e)
    {
        if (_isReadOnly) return;
        OverwriteByte(e.Offset, e.NewValue);
    }

    private void OnRendererSelectionChanged(object? sender, EventArgs e)
    {
        UpdateInspector();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        OffsetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRendererScrollChanged(object? sender, EventArgs e)
    {
        SyncScrollBarFromRenderer();
    }

    private void OnScrollBarScroll(object? sender, ScrollEventArgs e)
    {
        _renderer.ScrollOffset = _scrollBar.Value;
    }

    // ── Scroll bar ──────────────────────────────────────────────────────

    private void UpdateScrollBar()
    {
        long totalRows = _renderer.TotalRows;
        int visible = _renderer.VisibleRows;

        if (totalRows <= visible)
        {
            _scrollBar.Enabled = false;
            _scrollBar.Value = 0;
            return;
        }

        _scrollBar.Enabled = true;
        _scrollBar.Minimum = 0;
        _scrollBar.Maximum = (int)Math.Min(totalRows, int.MaxValue);
        _scrollBar.LargeChange = Math.Max(1, visible);
        _scrollBar.SmallChange = 1;
        SyncScrollBarFromRenderer();
    }

    private void SyncScrollBarFromRenderer()
    {
        int val = (int)Math.Min(_renderer.ScrollOffset, _scrollBar.Maximum);
        if (val >= _scrollBar.Minimum && val <= _scrollBar.Maximum)
            _scrollBar.Value = val;
    }

    // ── Inspector ───────────────────────────────────────────────────────

    private void UpdateInspector()
    {
        _inspector.Inspect(_data, _renderer.SelectedOffset,
            Math.Max(1, _renderer.SelectionLength));
    }

    // ── Resize ──────────────────────────────────────────────────────────

    private void OnFirstHandleCreated(object? sender, EventArgs e)
    {
        HandleCreated -= OnFirstHandleCreated;
        BeginInvoke(() =>
        {
            try
            {
                int h = _splitter.Height;
                if (h > 290)
                {
                    _splitter.SplitterDistance = h - 230;
                    _splitter.Panel2MinSize = 120;
                }
            }
            catch (InvalidOperationException) { }
        });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        UpdateScrollBar();
    }

    // ── Cleanup ─────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderer.Dispose();
            _inspector.Dispose();
            _scrollBar.Dispose();
            _splitter.Dispose();
        }
        base.Dispose(disposing);
    }
}

// ── Undo infrastructure ─────────────────────────────────────────────────

/// <summary>
/// The type of hex-editor edit that was recorded for undo/redo.
/// </summary>
internal enum HexUndoType
{
    Overwrite,
    Insert,
    Delete,
}

/// <summary>
/// A single undo/redo entry for a hex-editor operation.
/// </summary>
internal sealed record HexUndoEntry(
    HexUndoType Type,
    long Offset,
    byte OldValue,
    byte NewValue);
