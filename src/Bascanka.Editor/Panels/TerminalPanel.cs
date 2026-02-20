using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using Bascanka.Editor.Themes;
using Microsoft.Win32.SafeHandles;

namespace Bascanka.Editor.Panels;

/// <summary>
/// Embedded terminal panel using Windows ConPTY (Pseudo Console) API.
/// Spawns cmd.exe attached to a pseudo console, reads VT100 output,
/// renders a character grid, and forwards keyboard input.
/// </summary>
public partial class TerminalPanel : UserControl
{
    // ── Win32 Structures ──────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOEX
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars, dwFillAttribute;
        public int dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    // ── Win32 P/Invoke ────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES sa, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute,
        IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)0x00020016;

    // ── Color Palette (Campbell / Windows default) ────────────────────

    private static readonly Color[] Palette =
    [
        Color.FromArgb(12, 12, 12),      // 0  Black
        Color.FromArgb(197, 15, 31),     // 1  Red
        Color.FromArgb(19, 161, 14),     // 2  Green
        Color.FromArgb(193, 156, 0),     // 3  Yellow
        Color.FromArgb(0, 55, 218),      // 4  Blue
        Color.FromArgb(136, 23, 152),    // 5  Magenta
        Color.FromArgb(58, 150, 221),    // 6  Cyan
        Color.FromArgb(204, 204, 204),   // 7  White
        Color.FromArgb(118, 118, 118),   // 8  Bright Black
        Color.FromArgb(231, 72, 86),     // 9  Bright Red
        Color.FromArgb(22, 198, 12),     // 10 Bright Green
        Color.FromArgb(249, 241, 165),   // 11 Bright Yellow
        Color.FromArgb(59, 120, 255),    // 12 Bright Blue
        Color.FromArgb(180, 0, 158),     // 13 Bright Magenta
        Color.FromArgb(97, 214, 214),    // 14 Bright Cyan
        Color.FromArgb(242, 242, 242),   // 15 Bright White
    ];

    // ── Cell Attribute ────────────────────────────────────────────────

    private struct CellAttr
    {
        public byte Fg, Bg;
        public bool Bold, Underline, Reverse;
    }

    // ── Fields ────────────────────────────────────────────────────────

    // ConPTY handles
    private IntPtr _hPC, _hProcess, _hThread;
    private FileStream? _inputStream;
    private FileStream? _outputStream;
    private Thread? _readThread;
    private volatile bool _isRunning;
    private ITheme? _theme;

    // Screen buffer
    private char[] _chars = [];
    private CellAttr[] _attrs = [];
    private int _cols = 80, _rows = 24;

    // Cursor & scroll region
    private int _cursorRow, _cursorCol;
    private CellAttr _currentAttr;
    private bool _cursorVisible = true;
    private int _savedCursorRow, _savedCursorCol;
    private int _scrollTop, _scrollBottom;

    // Wide-character continuation sentinel (placed in the trailing cell).
    private const char WideCharCont = '\uFFFF';

    // Scrollback
    private readonly List<char[]> _scrollbackChars = new();
    private readonly List<CellAttr[]> _scrollbackAttrs = new();
    private const int ScrollbackMax = 5000;
    private int _viewOffset; // 0 = live view

    // VT parser
    private enum VtState { Normal, Escape, Csi, Osc, OscEsc }
    private VtState _vtState;
    private readonly StringBuilder _csiParams = new();
    private bool _csiPrivate;

    // Rendering
    public const int DefaultTerminalPadding = 6;
    public static int ConfigTerminalPadding { get; set; } = DefaultTerminalPadding;
    private Font _termFont;
    private int _cellW, _cellH;
    private readonly System.Windows.Forms.Timer _caretTimer;
    private bool _caretOn = true;

    // ── Construction ──────────────────────────────────────────────────

    public TerminalPanel()
    {
        Dock = DockStyle.Fill;
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);

        _termFont = new Font("Consolas", 10f, FontStyle.Regular);
        MeasureCell();

        _currentAttr = new CellAttr { Fg = 7, Bg = 0 };
        _scrollBottom = _rows - 1;
        InitBuffer();

        _caretTimer = new System.Windows.Forms.Timer { Interval = 530 };
        _caretTimer.Tick += (_, _) => { _caretOn = !_caretOn; InvalidateCursor(); };
        _caretTimer.Start();
    }

    // ── Public API ────────────────────────────────────────────────────

    public ITheme? Theme { get => _theme; set => _theme = value; }

    public void Start(string? workingDirectory = null)
    {
        if (_isRunning) return;
        try { StartConPty(workingDirectory); }
        catch { /* ConPTY unavailable */ }
    }

    public void Stop()
    {
        if (_hPC == IntPtr.Zero && _hProcess == IntPtr.Zero) return;
        _isRunning = false;

        try { _inputStream?.Dispose(); } catch { }
        _inputStream = null;

        try { _outputStream?.Dispose(); } catch { }
        _outputStream = null;

        // Read thread exits almost instantly after output stream is disposed.
        _readThread?.Join(200);
        _readThread = null;

        if (_hPC != IntPtr.Zero) { ClosePseudoConsole(_hPC); _hPC = IntPtr.Zero; }
        if (_hProcess != IntPtr.Zero)
        {
            TerminateProcess(_hProcess, 0);
            CloseHandle(_hProcess);
            _hProcess = IntPtr.Zero;
        }
        if (_hThread != IntPtr.Zero) { CloseHandle(_hThread); _hThread = IntPtr.Zero; }
    }

    public void FocusTerminal() => Focus();

    /// <summary>No-op — directory is set at <see cref="Start"/> time.</summary>
    public void SetWorkingDirectory(string? path) { }

    // ── ConPTY Setup ──────────────────────────────────────────────────

    private void StartConPty(string? workingDirectory)
    {
        RecalcSize();
        var size = new COORD { X = (short)_cols, Y = (short)_rows };

        var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
        if (!CreatePipe(out var pipeInRead, out var pipeInWrite, ref sa, 0))
            throw new InvalidOperationException("CreatePipe (in) failed");
        if (!CreatePipe(out var pipeOutRead, out var pipeOutWrite, ref sa, 0))
        {
            CloseHandle(pipeInRead); CloseHandle(pipeInWrite);
            throw new InvalidOperationException("CreatePipe (out) failed");
        }

        int hr = CreatePseudoConsole(size, pipeInRead, pipeOutWrite, 0, out _hPC);
        CloseHandle(pipeInRead);
        CloseHandle(pipeOutWrite);
        if (hr != 0)
        {
            CloseHandle(pipeInWrite); CloseHandle(pipeOutRead);
            throw new InvalidOperationException($"CreatePseudoConsole: 0x{hr:X8}");
        }

        IntPtr attrList = IntPtr.Zero;
        try
        {
            IntPtr attrSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
            attrList = Marshal.AllocHGlobal(attrSize);
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize))
                throw new InvalidOperationException("InitializeProcThreadAttributeList failed");
            if (!UpdateProcThreadAttribute(attrList, 0,
                    PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _hPC,
                    (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException("UpdateProcThreadAttribute failed");

            var si = new STARTUPINFOEX
            {
                cb = Marshal.SizeOf<STARTUPINFOEX>(),
                lpAttributeList = attrList
            };
            string dir = !string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory)
                ? workingDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!CreateProcessW(null, "cmd.exe", IntPtr.Zero, IntPtr.Zero,
                    false, EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero,
                    string.IsNullOrEmpty(dir) ? null : dir,
                    ref si, out var pi))
                throw new InvalidOperationException($"CreateProcessW: {Marshal.GetLastWin32Error()}");

            _hProcess = pi.hProcess;
            _hThread = pi.hThread;
        }
        catch
        {
            if (attrList != IntPtr.Zero) { DeleteProcThreadAttributeList(attrList); Marshal.FreeHGlobal(attrList); }
            CloseHandle(pipeInWrite); CloseHandle(pipeOutRead);
            ClosePseudoConsole(_hPC); _hPC = IntPtr.Zero;
            throw;
        }
        DeleteProcThreadAttributeList(attrList);
        Marshal.FreeHGlobal(attrList);

        _inputStream = new FileStream(new SafeFileHandle(pipeInWrite, true), FileAccess.Write);
        _outputStream = new FileStream(new SafeFileHandle(pipeOutRead, true), FileAccess.Read);
        _isRunning = true;

        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "ConPTY-Read" };
        _readThread.Start();
    }

    // ── Output Reading ────────────────────────────────────────────────

    private void ReadLoop()
    {
        var buf = new byte[4096];
        try
        {
            while (_isRunning && _outputStream is not null)
            {
                int n = _outputStream.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                string text = Encoding.UTF8.GetString(buf, 0, n);

                if (IsHandleCreated && !IsDisposed)
                {
                    try { BeginInvoke(() => ProcessVtOutput(text)); }
                    catch { break; }
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        _isRunning = false;
    }

    // ── VT100 Parser ──────────────────────────────────────────────────

    private void ProcessVtOutput(string text)
    {
        foreach (char c in text)
        {
            switch (_vtState)
            {
                case VtState.Normal:  HandleNormal(c); break;
                case VtState.Escape:  HandleEscape(c); break;
                case VtState.Csi:     HandleCsi(c);    break;
                case VtState.Osc:     HandleOsc(c);    break;
                case VtState.OscEsc:  _vtState = VtState.Normal; break; // ST
            }
        }
        _viewOffset = 0;
        Invalidate();
    }

    private void HandleNormal(char c)
    {
        switch (c)
        {
            case '\x1B': _vtState = VtState.Escape; break;
            case '\r':   _cursorCol = 0; break;
            case '\n':   LineFeed(); break;
            case '\b':   if (_cursorCol > 0) _cursorCol--; break;
            case '\t':   _cursorCol = Math.Min((_cursorCol / 8 + 1) * 8, _cols - 1); break;
            case '\x07': break; // BEL
            default:
                if (c >= ' ') PutChar(c);
                break;
        }
    }

    private void HandleEscape(char c)
    {
        _vtState = VtState.Normal;
        switch (c)
        {
            case '[':
                _vtState = VtState.Csi;
                _csiParams.Clear();
                _csiPrivate = false;
                break;
            case ']': _vtState = VtState.Osc; break;
            case '7': _savedCursorRow = _cursorRow; _savedCursorCol = _cursorCol; break;
            case '8': _cursorRow = _savedCursorRow; _cursorCol = _savedCursorCol; break;
            case 'M': // Reverse index
                if (_cursorRow == _scrollTop) ScrollDown();
                else if (_cursorRow > 0) _cursorRow--;
                break;
            case 'D': LineFeed(); break;
            case 'E': _cursorCol = 0; LineFeed(); break;
            case 'c': ResetTerminal(); break;
        }
    }

    private void HandleCsi(char c)
    {
        if (c == '?') { _csiPrivate = true; return; }
        if ((c >= '0' && c <= '9') || c == ';') { _csiParams.Append(c); return; }
        ExecuteCsi(c);
        _vtState = VtState.Normal;
    }

    private void HandleOsc(char c)
    {
        if (c == '\x07') _vtState = VtState.Normal;      // BEL terminates
        else if (c == '\x1B') _vtState = VtState.OscEsc;  // ESC \ terminates
    }

    // ── CSI Execution ─────────────────────────────────────────────────

    private int[] GetParams()
    {
        if (_csiParams.Length == 0) return [];
        var parts = _csiParams.ToString().Split(';');
        var r = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) int.TryParse(parts[i], out r[i]);
        return r;
    }

    private void ExecuteCsi(char cmd)
    {
        var p = GetParams();
        int p0 = p.Length > 0 && p[0] > 0 ? p[0] : 1;
        int p1 = p.Length > 1 && p[1] > 0 ? p[1] : 1;

        if (_csiPrivate)
        {
            int v = p.Length > 0 ? p[0] : 0;
            if (cmd == 'h' && v == 25) _cursorVisible = true;
            if (cmd == 'l' && v == 25) _cursorVisible = false;
            return;
        }

        switch (cmd)
        {
            case 'A': _cursorRow = Math.Max(_scrollTop, _cursorRow - p0); break;
            case 'B': _cursorRow = Math.Min(_scrollBottom, _cursorRow + p0); break;
            case 'C': _cursorCol = Math.Min(_cols - 1, _cursorCol + p0); break;
            case 'D': _cursorCol = Math.Max(0, _cursorCol - p0); break;
            case 'E': _cursorCol = 0; _cursorRow = Math.Min(_scrollBottom, _cursorRow + p0); break;
            case 'F': _cursorCol = 0; _cursorRow = Math.Max(_scrollTop, _cursorRow - p0); break;
            case 'G': _cursorCol = Math.Clamp(p0 - 1, 0, _cols - 1); break;
            case 'H' or 'f':
                _cursorRow = Math.Clamp(p0 - 1, 0, _rows - 1);
                _cursorCol = Math.Clamp(p1 - 1, 0, _cols - 1);
                break;
            case 'J': EraseDisplay(p.Length > 0 ? p[0] : 0); break;
            case 'K': EraseLine(p.Length > 0 ? p[0] : 0); break;
            case 'L': InsertLines(p0); break;
            case 'M': DeleteLines(p0); break;
            case 'P': DeleteChars(p0); break;
            case '@': InsertChars(p0); break;
            case 'S': for (int i = 0; i < p0; i++) ScrollUp(); break;
            case 'T': for (int i = 0; i < p0; i++) ScrollDown(); break;
            case 'd': _cursorRow = Math.Clamp(p0 - 1, 0, _rows - 1); break;
            case 'm': ExecuteSgr(p); break;
            case 'r':
                _scrollTop = Math.Clamp(p0 - 1, 0, _rows - 1);
                _scrollBottom = Math.Clamp((p.Length > 1 && p[1] > 0 ? p[1] : _rows) - 1, 0, _rows - 1);
                _cursorRow = _scrollTop; _cursorCol = 0;
                break;
            case 's': _savedCursorRow = _cursorRow; _savedCursorCol = _cursorCol; break;
            case 'u': _cursorRow = _savedCursorRow; _cursorCol = _savedCursorCol; break;
            case 'X': // Erase characters
                for (int i = 0; i < p0 && _cursorCol + i < _cols; i++)
                {
                    int idx = _cursorRow * _cols + _cursorCol + i;
                    _chars[idx] = ' '; _attrs[idx] = _currentAttr;
                }
                break;
        }
    }

    // ── SGR (Select Graphic Rendition) ────────────────────────────────

    private void ExecuteSgr(int[] p)
    {
        if (p.Length == 0) { _currentAttr = new CellAttr { Fg = 7, Bg = 0 }; return; }

        for (int i = 0; i < p.Length; i++)
        {
            switch (p[i])
            {
                case 0:  _currentAttr = new CellAttr { Fg = 7, Bg = 0 }; break;
                case 1:  _currentAttr.Bold = true; break;
                case 4:  _currentAttr.Underline = true; break;
                case 7:  _currentAttr.Reverse = true; break;
                case 22: _currentAttr.Bold = false; break;
                case 24: _currentAttr.Underline = false; break;
                case 27: _currentAttr.Reverse = false; break;
                case >= 30 and <= 37: _currentAttr.Fg = (byte)(p[i] - 30); break;
                case 38:
                    if (i + 2 < p.Length && p[i + 1] == 5) { _currentAttr.Fg = Map256(p[i + 2]); i += 2; }
                    else if (i + 4 < p.Length && p[i + 1] == 2) i += 4;
                    break;
                case 39: _currentAttr.Fg = 7; break;
                case >= 40 and <= 47: _currentAttr.Bg = (byte)(p[i] - 40); break;
                case 48:
                    if (i + 2 < p.Length && p[i + 1] == 5) { _currentAttr.Bg = Map256(p[i + 2]); i += 2; }
                    else if (i + 4 < p.Length && p[i + 1] == 2) i += 4;
                    break;
                case 49: _currentAttr.Bg = 0; break;
                case >= 90 and <= 97:   _currentAttr.Fg = (byte)(p[i] - 90 + 8); break;
                case >= 100 and <= 107: _currentAttr.Bg = (byte)(p[i] - 100 + 8); break;
            }
        }
    }

    private static byte Map256(int c)
    {
        if (c < 16) return (byte)c;
        if (c >= 232) { int g = (c - 232) * 10 + 8; return g < 128 ? (byte)0 : (byte)7; }
        return (byte)(c < 128 ? 0 : 7);
    }

    // ── Buffer Operations ─────────────────────────────────────────────

    private void InitBuffer()
    {
        int sz = _rows * _cols;
        _chars = new char[sz];
        _attrs = new CellAttr[sz];
        Array.Fill(_chars, ' ');
        Array.Fill(_attrs, new CellAttr { Fg = 7, Bg = 0 });
    }

    private void PutChar(char c)
    {
        bool wide = IsFullWidth(c);

        if (wide && _cursorCol >= _cols - 1)
        {
            // Wide char won't fit on the remainder of this line — blank
            // the last cell (if any) and wrap to the next line.
            if (_cursorCol < _cols)
            {
                int idx0 = _cursorRow * _cols + _cursorCol;
                _chars[idx0] = ' '; _attrs[idx0] = _currentAttr;
            }
            _cursorCol = 0; LineFeed();
        }
        else if (_cursorCol >= _cols)
        {
            _cursorCol = 0; LineFeed();
        }

        int idx = _cursorRow * _cols + _cursorCol;

        // If we're overwriting the trailing half of a previous wide char,
        // blank its leading half so it doesn't render as a partial glyph.
        if (_chars[idx] == WideCharCont && _cursorCol > 0)
            _chars[idx - 1] = ' ';

        // If we're overwriting the leading half of a wide char, blank
        // its trailing continuation cell.
        if (_cursorCol + 1 < _cols && _chars[idx + 1] == WideCharCont)
            _chars[idx + 1] = ' ';

        _chars[idx] = c;
        _attrs[idx] = _currentAttr;
        _cursorCol++;

        if (wide && _cursorCol < _cols)
        {
            int idx2 = _cursorRow * _cols + _cursorCol;
            // Same cleanup for the continuation cell.
            if (_cursorCol + 1 < _cols && _chars[idx2 + 1] == WideCharCont)
                _chars[idx2 + 1] = ' ';
            _chars[idx2] = WideCharCont;
            _attrs[idx2] = _currentAttr;
            _cursorCol++;
        }
    }

    /// <summary>
    /// Returns true for characters that occupy two columns in a terminal
    /// (CJK ideographs, Hangul syllables, fullwidth forms, etc.).
    /// Based on Unicode East Asian Width categories W and F.
    /// </summary>
    private static bool IsFullWidth(char c)
    {
        if (c < 0x1100) return false;
        return (c <= 0x115F) ||                          // Hangul Jamo
               (c >= 0x2E80 && c <= 0x303E) ||           // CJK Radicals, Kangxi, Symbols & Punctuation
               (c >= 0x3041 && c <= 0x33BF) ||           // Hiragana, Katakana, Bopomofo, Hangul Compat Jamo, Kanbun
               (c >= 0x3400 && c <= 0x4DBF) ||           // CJK Extension A
               (c >= 0x4E00 && c <= 0xA4CF) ||           // CJK Unified Ideographs, Yi Syllables/Radicals
               (c >= 0xA960 && c <= 0xA97F) ||           // Hangul Jamo Extended-A
               (c >= 0xAC00 && c <= 0xD7AF) ||           // Hangul Syllables
               (c >= 0xF900 && c <= 0xFAFF) ||           // CJK Compatibility Ideographs
               (c >= 0xFE10 && c <= 0xFE6F) ||           // CJK Compatibility Forms, Small Form Variants
               (c >= 0xFF01 && c <= 0xFF60) ||           // Fullwidth Forms
               (c >= 0xFFE0 && c <= 0xFFE6);            // Fullwidth Signs
    }

    private void LineFeed()
    {
        if (_cursorRow == _scrollBottom) ScrollUp();
        else if (_cursorRow < _rows - 1) _cursorRow++;
    }

    private void ScrollUp()
    {
        // Save top line to scrollback
        if (_scrollTop == 0)
        {
            var lc = new char[_cols]; var la = new CellAttr[_cols];
            Array.Copy(_chars, 0, lc, 0, _cols);
            Array.Copy(_attrs, 0, la, 0, _cols);
            _scrollbackChars.Add(lc); _scrollbackAttrs.Add(la);
            if (_scrollbackChars.Count > ScrollbackMax)
            { _scrollbackChars.RemoveAt(0); _scrollbackAttrs.RemoveAt(0); }
        }

        int start = _scrollTop * _cols, end = _scrollBottom * _cols;
        Array.Copy(_chars, start + _cols, _chars, start, end - start);
        Array.Copy(_attrs, start + _cols, _attrs, start, end - start);
        Array.Fill(_chars, ' ', end, _cols);
        Array.Fill(_attrs, new CellAttr { Fg = 7, Bg = 0 }, end, _cols);
    }

    private void ScrollDown()
    {
        int start = _scrollTop * _cols, end = _scrollBottom * _cols;
        Array.Copy(_chars, start, _chars, start + _cols, end - start);
        Array.Copy(_attrs, start, _attrs, start + _cols, end - start);
        Array.Fill(_chars, ' ', start, _cols);
        Array.Fill(_attrs, new CellAttr { Fg = 7, Bg = 0 }, start, _cols);
    }

    private void EraseDisplay(int mode)
    {
        var d = new CellAttr { Fg = 7, Bg = 0 };
        switch (mode)
        {
            case 0:
                int s0 = _cursorRow * _cols + _cursorCol;
                Array.Fill(_chars, ' ', s0, _chars.Length - s0);
                Array.Fill(_attrs, d, s0, _attrs.Length - s0);
                break;
            case 1:
                int c1 = _cursorRow * _cols + _cursorCol + 1;
                Array.Fill(_chars, ' ', 0, c1);
                Array.Fill(_attrs, d, 0, c1);
                break;
            case 2 or 3:
                Array.Fill(_chars, ' ');
                Array.Fill(_attrs, d);
                if (mode == 3) { _scrollbackChars.Clear(); _scrollbackAttrs.Clear(); }
                break;
        }
    }

    private void EraseLine(int mode)
    {
        var d = new CellAttr { Fg = 7, Bg = 0 };
        int rs = _cursorRow * _cols;
        switch (mode)
        {
            case 0: Array.Fill(_chars, ' ', rs + _cursorCol, _cols - _cursorCol);
                    Array.Fill(_attrs, d, rs + _cursorCol, _cols - _cursorCol); break;
            case 1: Array.Fill(_chars, ' ', rs, _cursorCol + 1);
                    Array.Fill(_attrs, d, rs, _cursorCol + 1); break;
            case 2: Array.Fill(_chars, ' ', rs, _cols);
                    Array.Fill(_attrs, d, rs, _cols); break;
        }
    }

    private void InsertLines(int n)
    {
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;
        var d = new CellAttr { Fg = 7, Bg = 0 };
        for (int i = 0; i < n; i++)
        {
            int from = _cursorRow * _cols, to = _scrollBottom * _cols;
            if (to > from) { Array.Copy(_chars, from, _chars, from + _cols, to - from);
                             Array.Copy(_attrs, from, _attrs, from + _cols, to - from); }
            Array.Fill(_chars, ' ', from, _cols); Array.Fill(_attrs, d, from, _cols);
        }
    }

    private void DeleteLines(int n)
    {
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;
        var d = new CellAttr { Fg = 7, Bg = 0 };
        for (int i = 0; i < n; i++)
        {
            int from = _cursorRow * _cols, to = _scrollBottom * _cols;
            if (to > from) { Array.Copy(_chars, from + _cols, _chars, from, to - from);
                             Array.Copy(_attrs, from + _cols, _attrs, from, to - from); }
            Array.Fill(_chars, ' ', to, _cols); Array.Fill(_attrs, d, to, _cols);
        }
    }

    private void DeleteChars(int n)
    {
        int rs = _cursorRow * _cols, pos = rs + _cursorCol, end = rs + _cols;
        int move = end - pos - n;
        if (move > 0) { Array.Copy(_chars, pos + n, _chars, pos, move);
                        Array.Copy(_attrs, pos + n, _attrs, pos, move); }
        int cs = Math.Max(pos, end - n);
        Array.Fill(_chars, ' ', cs, end - cs);
        Array.Fill(_attrs, new CellAttr { Fg = 7, Bg = 0 }, cs, end - cs);
    }

    private void InsertChars(int n)
    {
        int rs = _cursorRow * _cols, pos = rs + _cursorCol, end = rs + _cols;
        int move = end - pos - n;
        if (move > 0) { Array.Copy(_chars, pos, _chars, pos + n, move);
                        Array.Copy(_attrs, pos, _attrs, pos + n, move); }
        Array.Fill(_chars, ' ', pos, Math.Min(n, end - pos));
        Array.Fill(_attrs, new CellAttr { Fg = 7, Bg = 0 }, pos, Math.Min(n, end - pos));
    }

    private void ResetTerminal()
    {
        _currentAttr = new CellAttr { Fg = 7, Bg = 0 };
        _cursorRow = _cursorCol = 0;
        _scrollTop = 0; _scrollBottom = _rows - 1;
        _cursorVisible = true;
        InitBuffer();
    }

    // ── Cell Measurement ──────────────────────────────────────────────

    private void MeasureCell()
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var proposed = new Size(int.MaxValue, int.MaxValue);
        // Measure with TextRenderer (GDI) to match DrawText rendering.
        // Use difference of two lengths to cancel out any fixed padding.
        int w1 = TextRenderer.MeasureText("M", _termFont, proposed, flags).Width;
        int w2 = TextRenderer.MeasureText("MM", _termFont, proposed, flags).Width;
        _cellW = Math.Max(1, w2 - w1);
        _cellH = Math.Max(1, TextRenderer.MeasureText("Mj", _termFont, proposed, flags).Height);
    }

    private void RecalcSize()
    {
        int pad = ConfigTerminalPadding;
        int usableW = Width - pad * 2;
        int usableH = Height - pad * 2;
        if (usableW <= 0 || usableH <= 0 || _cellW <= 0 || _cellH <= 0) return;
        int nc = Math.Max(1, usableW / _cellW);
        int nr = Math.Max(1, usableH / _cellH);
        if (nc == _cols && nr == _rows) return;

        int oc = _cols, or2 = _rows;
        var oldC = _chars; var oldA = _attrs;
        _cols = nc; _rows = nr;
        InitBuffer();

        int cr = Math.Min(or2, _rows), cc = Math.Min(oc, _cols);
        for (int r = 0; r < cr; r++)
        {
            Array.Copy(oldC, r * oc, _chars, r * _cols, cc);
            Array.Copy(oldA, r * oc, _attrs, r * _cols, cc);
        }

        _cursorRow = Math.Min(_cursorRow, _rows - 1);
        _cursorCol = Math.Min(_cursorCol, _cols - 1);
        _scrollTop = 0; _scrollBottom = _rows - 1;
    }

    // ── Input ─────────────────────────────────────────────────────────

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right
                or Keys.Tab or Keys.Enter or Keys.Escape => true,
            _ => base.IsInputKey(keyData)
        };

    /// <summary>
    /// Intercepts Ctrl+key combinations so they reach the terminal
    /// instead of being consumed by form-level menu shortcuts.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_isRunning && Focused)
        {
            var mod = keyData & Keys.Modifiers;
            var key = keyData & Keys.KeyCode;

            if (mod == Keys.Control && key >= Keys.A && key <= Keys.Z)
            {
                if (key == Keys.V)
                {
                    string? clip = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(clip))
                        SendInput(Encoding.UTF8.GetBytes(clip));
                }
                else
                {
                    SendInput([(byte)(key - Keys.A + 1)]);
                }
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_inputStream is null || !_isRunning) { base.OnKeyDown(e); return; }

        byte[]? data = e.KeyCode switch
        {
            Keys.Up       => "\x1b[A"u8.ToArray(),
            Keys.Down     => "\x1b[B"u8.ToArray(),
            Keys.Right    => "\x1b[C"u8.ToArray(),
            Keys.Left     => "\x1b[D"u8.ToArray(),
            Keys.Home     => "\x1b[H"u8.ToArray(),
            Keys.End      => "\x1b[F"u8.ToArray(),
            Keys.Insert   => "\x1b[2~"u8.ToArray(),
            Keys.Delete   => "\x1b[3~"u8.ToArray(),
            Keys.PageUp   => "\x1b[5~"u8.ToArray(),
            Keys.PageDown => "\x1b[6~"u8.ToArray(),
            Keys.F1       => "\x1bOP"u8.ToArray(),
            Keys.F2       => "\x1bOQ"u8.ToArray(),
            Keys.F3       => "\x1bOR"u8.ToArray(),
            Keys.F4       => "\x1bOS"u8.ToArray(),
            Keys.F5       => "\x1b[15~"u8.ToArray(),
            Keys.F6       => "\x1b[17~"u8.ToArray(),
            Keys.F7       => "\x1b[18~"u8.ToArray(),
            Keys.F8       => "\x1b[19~"u8.ToArray(),
            Keys.F9       => "\x1b[20~"u8.ToArray(),
            Keys.F10      => "\x1b[21~"u8.ToArray(),
            Keys.F11      => "\x1b[23~"u8.ToArray(),
            Keys.F12      => "\x1b[24~"u8.ToArray(),
            Keys.Enter    => "\r"u8.ToArray(),
            Keys.Back     => [0x7F],
            Keys.Escape   => "\x1b"u8.ToArray(),
            Keys.Tab      => "\t"u8.ToArray(),
            _             => null,
        };

        if (data is not null)
        {
            SendInput(data);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (_inputStream is null || !_isRunning) { base.OnKeyPress(e); return; }
        if (e.KeyChar >= ' ')
        {
            SendInput(Encoding.UTF8.GetBytes([e.KeyChar]));
            e.Handled = true;
        }
    }

    private void SendInput(byte[] data)
    {
        try { _inputStream?.Write(data); _inputStream?.Flush(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    // ── Mouse ─────────────────────────────────────────────────────────

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _viewOffset = Math.Clamp(_viewOffset + (e.Delta > 0 ? 3 : -3), 0, _scrollbackChars.Count);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Right && _isRunning)
        {
            string? clip = Clipboard.GetText();
            if (!string.IsNullOrEmpty(clip))
                SendInput(Encoding.UTF8.GetBytes(clip));
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int pad = ConfigTerminalPadding;
        Color defBg = Palette[0], defFg = Palette[7];
        g.Clear(defBg);

        for (int row = 0; row < _rows; row++)
        {
            int y = pad + row * _cellH;
            if (y > Height) break;

            char[]? srcC; CellAttr[]? srcA; int off;

            if (_viewOffset > 0)
            {
                int sbRow = _scrollbackChars.Count - _viewOffset + row;
                if (sbRow < 0) continue;
                if (sbRow < _scrollbackChars.Count)
                { srcC = _scrollbackChars[sbRow]; srcA = _scrollbackAttrs[sbRow]; off = 0; }
                else
                {
                    int br = sbRow - _scrollbackChars.Count;
                    if (br >= _rows) continue;
                    srcC = _chars; srcA = _attrs; off = br * _cols;
                }
            }
            else
            { srcC = _chars; srcA = _attrs; off = row * _cols; }

            int colsInRow = Math.Min(_cols, srcC.Length - off);
            if (colsInRow <= 0) continue;

            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

            // First pass: draw background rectangles (batched by attribute)
            for (int col = 0; col < colsInRow;)
            {
                var attr = srcA[off + col];
                int run = 1;
                while (col + run < colsInRow)
                {
                    var na = srcA[off + col + run];
                    if (na.Fg != attr.Fg || na.Bg != attr.Bg ||
                        na.Bold != attr.Bold || na.Reverse != attr.Reverse) break;
                    run++;
                }
                Color bg = Palette[attr.Bg];
                if (attr.Reverse) bg = Palette[attr.Bold && attr.Fg < 8 ? attr.Fg + 8 : attr.Fg];
                if (bg != defBg)
                {
                    using var bgBrush = new SolidBrush(bg);
                    g.FillRectangle(bgBrush, pad + col * _cellW, y, run * _cellW, _cellH);
                }
                col += run;
            }

            // Second pass: draw each character at its exact grid position
            for (int col = 0; col < colsInRow; col++)
            {
                char ch = srcC[off + col];
                if (ch == ' ' || ch == '\0' || ch == WideCharCont) continue;
                var attr = srcA[off + col];
                Color fg = Palette[attr.Bold && attr.Fg < 8 ? attr.Fg + 8 : attr.Fg];
                if (attr.Reverse) fg = Palette[attr.Bg];

                if (IsFullWidth(ch))
                {
                    // Draw the wide glyph into a 2-cell-wide rectangle.
                    var rect = new Rectangle(pad + col * _cellW, y, _cellW * 2, _cellH);
                    TextRenderer.DrawText(g, ch.ToString(), _termFont, rect, fg, flags);
                }
                else
                {
                    TextRenderer.DrawText(g, ch.ToString(), _termFont,
                        new Point(pad + col * _cellW, y), fg, flags);
                }
            }
        }

        // Cursor
        if (_cursorVisible && _viewOffset == 0 && _caretOn && Focused)
        {
            int cx = pad + _cursorCol * _cellW, cy = pad + _cursorRow * _cellH;
            using var cb = new SolidBrush(Color.FromArgb(200, defFg));
            g.FillRectangle(cb, cx, cy, Math.Max(2, (int)(_cellW * 0.15f)), _cellH);
        }
    }

    private void InvalidateCursor()
    {
        if (_cursorCol < _cols && _cursorRow < _rows)
        {
            int pad = ConfigTerminalPadding;
            Invalidate(new Rectangle(pad + _cursorCol * _cellW, pad + _cursorRow * _cellH, _cellW + 1, _cellH + 1));
        }
    }

    // ── Resize & Focus ────────────────────────────────────────────────

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalcSize();
        if (_hPC != IntPtr.Zero && _isRunning)
            ResizePseudoConsole(_hPC, new COORD { X = (short)_cols, Y = (short)_rows });
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _caretOn = true;
        Invalidate();
    }

    // ── Disposal ──────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _caretTimer.Stop();
            _caretTimer.Dispose();
            Stop();
            _termFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
