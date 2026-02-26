using System.Drawing;
using System.Windows.Forms;
using Bascanka.Editor.Themes;

namespace Bascanka.Editor.HexEditor;

/// <summary>
/// Identifies which editing area the caret is currently in.
/// </summary>
public enum HexEditArea
{
    /// <summary>The hex-byte editing area.</summary>
    Hex,

    /// <summary>The ASCII character editing area.</summary>
    Ascii,
}

/// <summary>
/// Custom-painted surface that renders a hex editor display.
/// Layout per row: [Offset: 8-16 hex chars] [Gap] [16 hex bytes with space grouping] [Gap] [16 ASCII chars]
/// </summary>
public sealed class HexRenderer : Control
{
    // ── Configurable constants ──────────────────────────────────────────

    private const int OffsetColumnChars = 10;   // "00000000: "
    private const int GapWidth = 16;            // pixel gap between columns
    private const int CursorBlinkInterval = 530;

    // ── Fields ──────────────────────────────────────────────────────────

    private byte[] _data = [];
    private HashSet<long> _modifiedOffsets = [];
    private int _bytesPerRow = 16;
    private long _selectedOffset;
    private long _selectionLength;
    private int _currentNibble;  // 0 = high, 1 = low (within a hex byte)
    private HexEditArea _editArea = HexEditArea.Hex;
    private bool _isReadOnly;
    private long _scrollOffset;  // first visible row index
    private ITheme _theme;

    // Computed layout values
    private int _charWidth;
    private int _charHeight;
    private int _offsetColumnWidth;
    private int _hexColumnWidth;
    private int _asciiColumnWidth;
    private int _hexColumnStart;
    private int _asciiColumnStart;
    private int _visibleRows;

    // Cursor blinking
    private readonly System.Windows.Forms.Timer _cursorTimer;
    private bool _cursorVisible = true;

    // Mouse drag selection
    private bool _mouseSelecting;
    private long _mouseSelectAnchor;

    // ── Events ──────────────────────────────────────────────────────────

    /// <summary>Raised when the user edits a byte.</summary>
    public event EventHandler<HexEditEventArgs>? ByteEdited;

    /// <summary>Raised when the selection changes via mouse or keyboard.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when scrolling changes the visible region.</summary>
    public event EventHandler? ScrollChanged;

    // ── Construction ────────────────────────────────────────────────────

    public HexRenderer()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        _theme = new DarkTheme();

        _cursorTimer = new System.Windows.Forms.Timer { Interval = CursorBlinkInterval };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorVisible = !_cursorVisible;
            InvalidateCurrentByte();
        };
        _cursorTimer.Start();

        Font = new Font("Consolas", 10f, FontStyle.Regular);
        TabStop = true;
    }

    // ── Properties ──────────────────────────────────────────────────────

    /// <summary>The raw data being displayed.</summary>
    public byte[] Data
    {
        get => _data;
        set
        {
            _data = value ?? [];
            _modifiedOffsets.Clear();
            _selectedOffset = 0;
            _selectionLength = 0;
            _scrollOffset = 0;
            RecalculateLayout();
            Invalidate();
        }
    }

    /// <summary>Set of byte offsets that have been modified (rendered in red).</summary>
    public HashSet<long> ModifiedOffsets
    {
        get => _modifiedOffsets;
        set { _modifiedOffsets = value ?? []; Invalidate(); }
    }

    /// <summary>Number of bytes displayed per row.</summary>
    public int BytesPerRow
    {
        get => _bytesPerRow;
        set
        {
            _bytesPerRow = Math.Max(1, value);
            RecalculateLayout();
            Invalidate();
        }
    }

    /// <summary>Currently selected byte offset.</summary>
    public long SelectedOffset
    {
        get => _selectedOffset;
        set
        {
            long clamped = Math.Clamp(value, 0, Math.Max(0, _data.Length - 1));
            if (_selectedOffset == clamped) return;
            _selectedOffset = clamped;
            _currentNibble = 0;
            EnsureVisible(_selectedOffset);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            ResetCursorBlink();
            Invalidate();
        }
    }

    /// <summary>Number of bytes in the selection.</summary>
    public long SelectionLength
    {
        get => _selectionLength;
        set
        {
            _selectionLength = Math.Max(0, value);
            Invalidate();
        }
    }

    /// <summary>Which area (Hex or ASCII) is actively being edited.</summary>
    public HexEditArea EditArea
    {
        get => _editArea;
        set { _editArea = value; _currentNibble = 0; Invalidate(); }
    }

    /// <summary>When true, input that modifies bytes is ignored.</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => _isReadOnly = value;
    }

    /// <summary>First visible row index (driven by external scrollbar).</summary>
    public long ScrollOffset
    {
        get => _scrollOffset;
        set
        {
            long max = Math.Max(0, TotalRows - _visibleRows);
            long clamped = Math.Clamp(value, 0, max);
            if (_scrollOffset == clamped) return;
            _scrollOffset = clamped;
            ScrollChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    /// <summary>Total number of rows needed to display all data.</summary>
    public long TotalRows => _data.Length == 0 ? 1 : (_data.Length + _bytesPerRow - 1) / _bytesPerRow;

    /// <summary>Number of fully visible rows in the current control height.</summary>
    public int VisibleRows => _visibleRows;

    /// <summary>The theme used for colouring.</summary>
    public ITheme Theme
    {
        get => _theme;
        set { _theme = value ?? new DarkTheme(); Invalidate(); }
    }

    // ── Layout ──────────────────────────────────────────────────────────

    private void RecalculateLayout()
    {
        using Graphics g = CreateGraphics();
        SizeF charSize = g.MeasureString("W", Font, 0, StringFormat.GenericTypographic);
        _charWidth = (int)Math.Ceiling(charSize.Width);
        _charHeight = (int)Math.Ceiling(Font.GetHeight(g));
        if (_charHeight < 1) _charHeight = 14;
        if (_charWidth < 1) _charWidth = 8;

        // Offset column: "XXXXXXXX: " = OffsetColumnChars characters
        _offsetColumnWidth = _charWidth * OffsetColumnChars;

        // Hex column: each byte = "XX " (3 chars), extra space every 8 bytes
        int hexChars = _bytesPerRow * 3 + (_bytesPerRow / 8 - 1);
        if (hexChars < _bytesPerRow * 3) hexChars = _bytesPerRow * 3;
        _hexColumnWidth = _charWidth * hexChars;

        // ASCII column: one char per byte
        _asciiColumnWidth = _charWidth * _bytesPerRow;

        // Column starts
        _hexColumnStart = _offsetColumnWidth + GapWidth;
        _asciiColumnStart = _hexColumnStart + _hexColumnWidth + GapWidth;

        // Visible rows
        _visibleRows = Math.Max(1, (Height - 2) / _charHeight);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        RecalculateLayout();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
    }

    // ── Painting ────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.Clear(_theme.EditorBackground);

        if (_data.Length == 0) return;

        using Brush foregroundBrush = new SolidBrush(_theme.EditorForeground);
        using Brush offsetBrush = new SolidBrush(_theme.GutterForeground);
        using Brush selectionBgBrush = new SolidBrush(_theme.SelectionBackground);
        using Brush selectionFgBrush = new SolidBrush(_theme.SelectionForeground);
        using Brush modifiedBrush = new SolidBrush(_theme.ModifiedIndicator);
        using Brush caretBrush = new SolidBrush(_theme.CaretColor);
        using Pen caretPen = new(_theme.CaretColor, 1.5f);
        using StringFormat sf = new(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap,
        };

        long selStart = _selectedOffset;
        long selEnd = _selectedOffset + Math.Max(1, _selectionLength);

        for (int row = 0; row < _visibleRows; row++)
        {
            long rowIndex = _scrollOffset + row;
            if (rowIndex >= TotalRows) break;

            long byteOffset = rowIndex * _bytesPerRow;
            int y = row * _charHeight;

            // ---- Offset column ----
            string offsetStr = byteOffset.ToString("X8") + ": ";
            g.DrawString(offsetStr, Font, offsetBrush, 0, y, sf);

            // ---- Hex bytes and ASCII ----
            for (int col = 0; col < _bytesPerRow; col++)
            {
                long dataIndex = byteOffset + col;
                if (dataIndex >= _data.Length) break;

                byte b = _data[dataIndex];
                bool isSelected = dataIndex >= selStart && dataIndex < selEnd;
                bool isModified = _modifiedOffsets.Contains(dataIndex);
                bool isCursorByte = dataIndex == _selectedOffset;

                // Determine foreground colour
                Brush fg = isSelected ? selectionFgBrush :
                           isModified ? modifiedBrush : foregroundBrush;

                // ---- Hex area ----
                int hexX = _hexColumnStart + col * _charWidth * 3;
                // Extra space every 8 bytes
                if (col >= 8) hexX += _charWidth;

                if (isSelected)
                    g.FillRectangle(selectionBgBrush, hexX, y, _charWidth * 2, _charHeight);

                string hexStr = b.ToString("X2");
                g.DrawString(hexStr, Font, fg, hexX, y, sf);

                // Cursor in hex area
                if (isCursorByte && _editArea == HexEditArea.Hex && _cursorVisible && Focused)
                {
                    int nibbleX = hexX + _currentNibble * _charWidth;
                    g.DrawLine(caretPen, nibbleX, y, nibbleX, y + _charHeight);
                }

                // ---- ASCII area ----
                int asciiX = _asciiColumnStart + col * _charWidth;

                if (isSelected)
                    g.FillRectangle(selectionBgBrush, asciiX, y, _charWidth, _charHeight);

                char displayChar = b >= 0x20 && b < 0x7F ? (char)b : '.';
                g.DrawString(displayChar.ToString(), Font, fg, asciiX, y, sf);

                // Cursor in ASCII area
                if (isCursorByte && _editArea == HexEditArea.Ascii && _cursorVisible && Focused)
                {
                    g.DrawLine(caretPen, asciiX, y, asciiX, y + _charHeight);
                }
            }
        }
    }

    private void InvalidateCurrentByte()
    {
        if (_data.Length == 0) return;
        long row = _selectedOffset / _bytesPerRow - _scrollOffset;
        if (row < 0 || row >= _visibleRows) return;
        // Invalidate full row for simplicity
        Invalidate(new Rectangle(0, (int)row * _charHeight, Width, _charHeight));
    }

    private void ResetCursorBlink()
    {
        _cursorVisible = true;
        _cursorTimer.Stop();
        _cursorTimer.Start();
    }

    // ── Scrolling ───────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the given byte offset is visible in the viewport.
    /// </summary>
    public void EnsureVisible(long offset)
    {
        long row = offset / _bytesPerRow;
        if (row < _scrollOffset)
            ScrollOffset = row;
        else if (row >= _scrollOffset + _visibleRows)
            ScrollOffset = row - _visibleRows + 1;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int delta = e.Delta > 0 ? -3 : 3;
        ScrollOffset += delta;
    }

    // ── Mouse ───────────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        long offset = HitTest(e.Location, out HexEditArea area);
        if (offset < 0) return;

        _editArea = area;
        _currentNibble = 0;
        _selectedOffset = offset;
        _selectionLength = 0;
        _mouseSelecting = true;
        _mouseSelectAnchor = offset;
        ResetCursorBlink();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_mouseSelecting) return;

        long offset = HitTest(e.Location, out _);
        if (offset < 0) return;

        long start = Math.Min(_mouseSelectAnchor, offset);
        long end = Math.Max(_mouseSelectAnchor, offset);
        _selectedOffset = start;
        _selectionLength = end - start + 1;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _mouseSelecting = false;
    }

    /// <summary>
    /// Determines which byte offset a screen point maps to.
    /// Returns -1 if the point is outside the data area.
    /// </summary>
    private long HitTest(Point p, out HexEditArea area)
    {
        area = HexEditArea.Hex;
        int row = p.Y / _charHeight;
        long rowIndex = _scrollOffset + row;
        if (rowIndex >= TotalRows) return -1;

        long byteOffset = rowIndex * _bytesPerRow;

        // Check ASCII area first (it's to the right)
        if (p.X >= _asciiColumnStart && p.X < _asciiColumnStart + _asciiColumnWidth)
        {
            area = HexEditArea.Ascii;
            int col = (p.X - _asciiColumnStart) / _charWidth;
            col = Math.Clamp(col, 0, _bytesPerRow - 1);
            long offset = byteOffset + col;
            return offset < _data.Length ? offset : -1;
        }

        // Check hex area
        if (p.X >= _hexColumnStart && p.X < _hexColumnStart + _hexColumnWidth)
        {
            area = HexEditArea.Hex;
            // Account for extra space after byte 8
            int relX = p.X - _hexColumnStart;
            int col = relX / (_charWidth * 3);
            if (col >= 8) col = (relX - _charWidth) / (_charWidth * 3);
            col = Math.Clamp(col, 0, _bytesPerRow - 1);
            long offset = byteOffset + col;
            return offset < _data.Length ? offset : -1;
        }

        return -1;
    }

    // ── Keyboard ────────────────────────────────────────────────────────

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right
                or Keys.PageUp or Keys.PageDown
                or Keys.Home or Keys.End
                or Keys.Tab => true,
            _ => base.IsInputKey(keyData),
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_data.Length == 0) return;

        bool shift = e.Shift;        

        switch (e.KeyCode)
        {
            case Keys.Left:
                MoveCaret(-1, shift);
                e.Handled = true;
                break;

            case Keys.Right:
                MoveCaret(1, shift);
                e.Handled = true;
                break;

            case Keys.Up:
                MoveCaret(-_bytesPerRow, shift);
                e.Handled = true;
                break;

            case Keys.Down:
                MoveCaret(_bytesPerRow, shift);
                e.Handled = true;
                break;

            case Keys.PageUp:
                MoveCaret(-_bytesPerRow * _visibleRows, shift);
                e.Handled = true;
                break;

            case Keys.PageDown:
                MoveCaret(_bytesPerRow * _visibleRows, shift);
                e.Handled = true;
                break;

            case Keys.Home:
                if (e.Control)
                    SetCaretPosition(0, shift);
                else
                    SetCaretPosition(_selectedOffset - _selectedOffset % _bytesPerRow, shift);
                e.Handled = true;
                break;

            case Keys.End:
                if (e.Control)
                    SetCaretPosition(_data.Length - 1, shift);
                else
                {
                    long rowEnd = _selectedOffset - _selectedOffset % _bytesPerRow + _bytesPerRow - 1;
                    SetCaretPosition(Math.Min(rowEnd, _data.Length - 1), shift);
                }
                e.Handled = true;
                break;

            case Keys.Tab:
                _editArea = _editArea == HexEditArea.Hex ? HexEditArea.Ascii : HexEditArea.Hex;
                _currentNibble = 0;
                e.Handled = true;
                Invalidate();
                break;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (_isReadOnly || _data.Length == 0) return;

        if (_editArea == HexEditArea.Hex)
        {
            int nibbleValue = HexCharToValue(e.KeyChar);
            if (nibbleValue >= 0)
            {
                byte oldByte = _data[_selectedOffset];
                byte newByte;
                if (_currentNibble == 0)
                {
                    newByte = (byte)((nibbleValue << 4) | (oldByte & 0x0F));
                    _currentNibble = 1;
                }
                else
                {
                    newByte = (byte)((oldByte & 0xF0) | nibbleValue);
                    _currentNibble = 0;
                }

                ByteEdited?.Invoke(this, new HexEditEventArgs(_selectedOffset, oldByte, newByte));

                if (_currentNibble == 0 && _selectedOffset < _data.Length - 1)
                    SelectedOffset++;

                e.Handled = true;
                Invalidate();
            }
        }
        else // ASCII area
        {
            if (e.KeyChar >= 0x20 && e.KeyChar < 0x7F)
            {
                byte oldByte = _data[_selectedOffset];
                byte newByte = (byte)e.KeyChar;
                ByteEdited?.Invoke(this, new HexEditEventArgs(_selectedOffset, oldByte, newByte));

                if (_selectedOffset < _data.Length - 1)
                    SelectedOffset++;

                e.Handled = true;
                Invalidate();
            }
        }
    }

    private void MoveCaret(long delta, bool extendSelection)
    {
        long newOffset = Math.Clamp(_selectedOffset + delta, 0, Math.Max(0, _data.Length - 1));
        SetCaretPosition(newOffset, extendSelection);
    }

    private void SetCaretPosition(long newOffset, bool extendSelection)
    {
        long clamped = Math.Clamp(newOffset, 0, Math.Max(0, _data.Length - 1));

        if (extendSelection)
        {
            long anchor = _selectionLength > 0
                ? _selectedOffset
                : _selectedOffset;
            long start = Math.Min(anchor, clamped);
            long end = Math.Max(anchor, clamped);
            _selectedOffset = start;
            _selectionLength = end - start + 1;
        }
        else
        {
            _selectedOffset = clamped;
            _selectionLength = 0;
        }

        _currentNibble = 0;
        EnsureVisible(_selectedOffset);
        ResetCursorBlink();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private static int HexCharToValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    // ── Cleanup ─────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cursorTimer.Stop();
            _cursorTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
