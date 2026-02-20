using System.Text;
using System.Text.Json;
using Bascanka.Core.Buffer;
using Bascanka.Core.Encoding;
using Bascanka.Core.IO;
using Bascanka.Core.Syntax;
using Bascanka.Editor.Controls;
using Bascanka.Editor.Tabs;
using Bascanka.Editor.Themes;

namespace Bascanka.App;

/// <summary>
/// Silently writes document state to disk every 10 seconds, enabling full
/// workspace restoration after a crash or abnormal exit. Recovery data is
/// stored in <c>%AppData%\Bascanka\recovery\</c> and deleted on normal shutdown.
/// </summary>
public sealed class RecoveryManager : IDisposable
{
    // Binary format magic bytes and version for piece-table recovery files.
    private static readonly byte[] Magic = "BSRV"u8.ToArray();
    private const uint FormatVersion = 1;

    private static readonly string RecoveryDir = Path.Combine(
        SettingsManager.AppDataFolder, "recovery");

    private static readonly string ManifestPath = Path.Combine(RecoveryDir, "manifest.json");

    /// <summary>Default auto-save interval in seconds.</summary>
    public const int DefaultIntervalSeconds = 10;

    private readonly MainForm _form;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HashSet<Guid> _dirtyTabs = new();
    private bool _saving;
    private bool _disposed;

    public RecoveryManager(MainForm form)
    {
        _form = form;
        _timer = new System.Windows.Forms.Timer { Interval = DefaultIntervalSeconds * 1000 };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>Starts the periodic recovery timer.</summary>
    public void Start() => _timer.Start();

    /// <summary>Stops the periodic recovery timer.</summary>
    public void Stop() => _timer.Stop();

    /// <summary>
    /// Updates the auto-save interval. Takes effect on the next timer tick.
    /// </summary>
    public void SetInterval(int seconds)
    {
        _timer.Interval = Math.Max(1, seconds) * 1000;
    }

    /// <summary>
    /// Marks a tab as needing its content written on the next tick.
    /// Called from <see cref="MainForm.OnEditorContentChanged"/>.
    /// </summary>
    public void MarkDirty(Guid tabId) => _dirtyTabs.Add(tabId);

    /// <summary>
    /// Immediately writes all dirty tab content and the manifest.
    /// Called from <see cref="MainForm.OnFormClosing"/> to persist state on exit.
    /// </summary>
    public void ForceWrite() => OnTimerTick(null, EventArgs.Empty);

    /// <summary>
    /// Removes the recovery content file for a tab (e.g. after save or close).
    /// </summary>
    public void RemoveTabRecovery(Guid tabId)
    {
        _dirtyTabs.Remove(tabId);
        try
        {
            string contentPath = Path.Combine(RecoveryDir, $"{tabId}.content");
            if (File.Exists(contentPath))
                File.Delete(contentPath);
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Deletes the entire recovery directory. Called on normal shutdown.
    /// </summary>
    public void CleanUp()
    {
        try
        {
            if (Directory.Exists(RecoveryDir))
                Directory.Delete(RecoveryDir, recursive: true);
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Returns true if a recovery manifest exists from a previous session.
    /// </summary>
    public static bool HasRecoveryData() => File.Exists(ManifestPath);

    // ── Timer tick ───────────────────────────────────────────────────

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_saving) return;
        var tabs = _form.Tabs;
        if (tabs.Count == 0) return;

        _saving = true;
        try
        {
            Directory.CreateDirectory(RecoveryDir);

            foreach (var tab in tabs)
            {
                // Skip deferred (not loaded), binary, loading, and non-dirty tabs.
                if (tab.IsDeferredLoad) continue;
                if (tab.IsBinaryMode) continue;
                if (tab.IsLoading) continue;
                if (!tab.IsModified) continue;
                if (!_dirtyTabs.Contains(tab.Id)) continue;

                WriteTabContent(tab);
            }

            WriteManifest(tabs);
            _dirtyTabs.Clear();

            // Remove orphaned .content files from previous sessions whose
            // tab IDs no longer exist (e.g. after recovery restore with
            // preserved IDs, old files from closed tabs linger).
            CleanOrphanedContentFiles(tabs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recovery save failed: {ex.Message}");
        }
        finally
        {
            _saving = false;
        }
    }

    // ── Per-tab content writing ──────────────────────────────────────

    private void WriteTabContent(TabInfo tab)
    {
        string contentPath = Path.Combine(RecoveryDir, $"{tab.Id}.content");
        string tmpPath = contentPath + ".tmp";

        try
        {
            if (tab.Editor.IsMemoryMappedDocument)
                WritePiecesFormat(tab, tmpPath);
            else
                WriteTextFormat(tab, tmpPath);

            // Atomic swap.
            if (File.Exists(contentPath))
                File.Delete(contentPath);
            File.Move(tmpPath, contentPath);
        }
        catch
        {
            // Clean up partial write.
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
        }
    }

    private static void WriteTextFormat(TabInfo tab, string tmpPath)
    {
        string text = tab.Editor.Document.ToString();
        File.WriteAllText(tmpPath, text, new UTF8Encoding(false));
    }

    private static void WritePiecesFormat(TabInfo tab, string tmpPath)
    {
        var doc = tab.Editor.Document;
        string addBuffer = doc.GetAddBufferContents();
        var pieces = doc.GetPiecesInOrder();
        byte[] addBufferBytes = Encoding.UTF8.GetBytes(addBuffer);

        using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        bw.Write(Magic);                      // 4 bytes: "BSRV"
        bw.Write(FormatVersion);               // uint32
        bw.Write((long)addBufferBytes.Length); // int64: add buffer byte length
        bw.Write(addBufferBytes);              // raw UTF-8 add buffer
        bw.Write(pieces.Count);                // int32: piece count

        foreach (var p in pieces)
        {
            bw.Write((byte)p.BufferType);      // byte: 0=Original, 1=Add
            bw.Write(p.Start);                 // int64
            bw.Write(p.Length);                // int64
            bw.Write(p.LineFeeds);             // int32
        }
    }

    // ── Manifest writing ─────────────────────────────────────────────

    private void WriteManifest(IReadOnlyList<TabInfo> tabs)
    {
        string tmpPath = ManifestPath + ".tmp";

        var tabEntries = new List<Dictionary<string, object>>();

        foreach (var tab in tabs)
        {
            var entry = new Dictionary<string, object>
            {
                ["Id"] = tab.Id.ToString(),
                ["Title"] = tab.Title,
                ["IsModified"] = tab.IsModified,
                ["IsLargeFile"] = tab.Editor.IsMemoryMappedDocument,
            };

            if (tab.FilePath is not null)
                entry["Path"] = tab.FilePath;

            if (tab.IsBinaryMode)
                entry["IsBinaryMode"] = true;

            if (tab.IsDeferredLoad)
            {
                entry["IsDeferredLoad"] = true;
                entry["Caret"] = (long)tab.PendingCaret;
                entry["Scroll"] = tab.PendingScroll;
                entry["Zoom"] = tab.PendingZoom;
                if (tab.PendingWordWrap == true)
                    entry["WordWrap"] = 1;
                if (tab.PendingLanguage is not null)
                    entry["Language"] = tab.PendingLanguage;
                if (tab.PendingCustomProfileName is not null)
                    entry["CustomProfileName"] = tab.PendingCustomProfileName;
            }
            else
            {
                entry["Caret"] = tab.Editor.CaretOffset;
                entry["Scroll"] = (int)tab.Editor.ScrollMgr.FirstVisibleLine;
                entry["Zoom"] = tab.Editor.ZoomLevel;
                entry["Language"] = tab.Editor.Language;
                entry["LineEnding"] = tab.Editor.LineEnding;
                if (tab.Editor.WordWrap)
                    entry["WordWrap"] = 1;

                string? customProfile = tab.Editor.CustomProfileName;
                if (customProfile is not null)
                    entry["CustomProfileName"] = customProfile;

                if (tab.Editor.EncodingManager is { } em)
                {
                    entry["EncodingCodePage"] = em.CurrentEncoding.CodePage;
                    entry["HasBom"] = em.HasBom;
                }
            }

            // Determine recovery format for modified tabs.
            if (tab.IsModified && !tab.IsDeferredLoad && !tab.IsBinaryMode)
            {
                entry["Format"] = tab.Editor.IsMemoryMappedDocument ? "pieces" : "text";
            }

            // For piece-format recovery, store original file metadata for validation.
            if (tab.IsModified && tab.Editor.IsMemoryMappedDocument && tab.FilePath is not null)
            {
                try
                {
                    var fi = new FileInfo(tab.FilePath);
                    if (fi.Exists)
                    {
                        entry["OriginalFileSize"] = fi.Length;
                        entry["OriginalLastWrite"] = fi.LastWriteTimeUtc.ToString("O");
                    }
                }
                catch { /* best effort */ }
            }

            tabEntries.Add(entry);
        }

        // Window geometry.
        bool maximized = _form.WindowState == FormWindowState.Maximized;
        var bounds = maximized ? _form.RestoreBounds : _form.Bounds;

        // Search history.
        var history = Bascanka.Editor.Panels.FindReplacePanel.GetSearchHistory();

        var manifest = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["Version"] = 1,
            ["ActiveTab"] = _form.ActiveTabIndex,
            ["SearchHistory"] = history.ToArray(),
            ["Tabs"] = tabEntries,
            ["WindowHeight"] = bounds.Height,
            ["WindowMaximized"] = maximized ? 1 : 0,
            ["WindowWidth"] = bounds.Width,
            ["WindowX"] = bounds.X,
            ["WindowY"] = bounds.Y,
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(manifest, options);
        File.WriteAllText(tmpPath, json, new UTF8Encoding(false));

        if (File.Exists(ManifestPath))
            File.Delete(ManifestPath);
        File.Move(tmpPath, ManifestPath);
    }

    private static void CleanOrphanedContentFiles(IReadOnlyList<TabInfo> tabs)
    {
        try
        {
            var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tab in tabs)
                knownIds.Add(tab.Id.ToString());

            foreach (string file in Directory.EnumerateFiles(RecoveryDir, "*.content"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!knownIds.Contains(name))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { /* best effort */ }
    }

    // ── Restore ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads the recovery manifest and restores all tabs. Returns true if
    /// at least one tab was restored.
    /// </summary>
    public bool RestoreFromRecovery()
    {
        try
        {
            if (!File.Exists(ManifestPath)) return false;

            string json = File.ReadAllText(ManifestPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Restore window geometry.
            RestoreWindowGeometry(root);

            // Restore search history.
            RestoreSearchHistory(root);

            // Restore tabs.
            if (!root.TryGetProperty("Tabs", out var tabsEl) || tabsEl.ValueKind != JsonValueKind.Array)
                return false;

            int activeIndex = GetInt(root, "ActiveTab", 0);
            int tabCount = tabsEl.GetArrayLength();
            if (tabCount == 0) return false;

            int actualActiveIndex = -1;
            bool anyRestored = false;

            for (int i = 0; i < tabCount; i++)
            {
                var tabEl = tabsEl[i];

                bool restored = RestoreTab(tabEl, out bool isActive);
                if (restored)
                {
                    anyRestored = true;
                    if (i == activeIndex)
                        actualActiveIndex = _form.Tabs.Count - 1;
                }
            }

            // Activate the previously active tab.
            if (actualActiveIndex >= 0 && actualActiveIndex < _form.Tabs.Count)
                _form.ActivateTab(actualActiveIndex);
            else if (_form.Tabs.Count > 0)
                _form.ActivateTab(0);

            // Recovery files are kept — the timer will overwrite them on its
            // next tick with fresh state.  They are only deleted on normal shutdown.

            return anyRestored;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recovery restore failed: {ex.Message}");
            return false;
        }
    }

    private bool RestoreTab(JsonElement tabEl, out bool isActive)
    {
        isActive = false;
        string? idStr = GetString(tabEl, "Id");
        bool isModified = GetBool(tabEl, "IsModified");
        string? path = GetString(tabEl, "Path");
        string? format = GetString(tabEl, "Format");
        bool isBinary = GetBool(tabEl, "IsBinaryMode");
        bool isDeferred = GetBool(tabEl, "IsDeferredLoad");

        // Skip binary tabs — nothing to recover.
        if (isBinary && path is not null && File.Exists(path))
        {
            _form.OpenFile(path);
            return true;
        }

        // Deferred tabs — restore as deferred if file exists.
        if (isDeferred && path is not null && File.Exists(path))
        {
            int zoom = GetInt(tabEl, "Zoom", 0);
            int scroll = GetInt(tabEl, "Scroll", 0);
            long caret = GetLong(tabEl, "Caret", 0);
            bool wordWrap = GetInt(tabEl, "WordWrap", 0) != 0;
            _form.AddDeferredTab(path, zoom, scroll, (int)Math.Min(caret, int.MaxValue), wordWrap);
            return true;
        }

        // Unmodified tabs — just reopen normally.
        if (!isModified)
        {
            if (path is not null && File.Exists(path))
            {
                int zoom = GetInt(tabEl, "Zoom", 0);
                int scroll = GetInt(tabEl, "Scroll", 0);
                long caret = GetLong(tabEl, "Caret", 0);
                bool wordWrap = GetInt(tabEl, "WordWrap", 0) != 0;

                // Use deferred loading for non-active tabs.
                _form.AddDeferredTab(path, zoom, scroll, (int)Math.Min(caret, int.MaxValue), wordWrap);
                return true;
            }
            return false;
        }

        // ── Modified tabs: need recovery content ─────────────────

        Guid tabId;
        if (!Guid.TryParse(idStr, out tabId)) return false;

        string contentPath = Path.Combine(RecoveryDir, $"{tabId}.content");
        if (!File.Exists(contentPath))
        {
            // No content file — try to open the file normally if it exists.
            if (path is not null && File.Exists(path))
            {
                _form.OpenFile(path);
                return true;
            }
            return false;
        }

        if (format == "pieces")
            return RestorePiecesTab(tabEl, tabId, contentPath, path);

        // Default to text format.
        return RestoreTextTab(tabEl, tabId, contentPath, path);
    }

    private bool RestoreTextTab(JsonElement tabEl, Guid tabId, string contentPath, string? path)
    {
        try
        {
            string text = File.ReadAllText(contentPath, new UTF8Encoding(false));
            var pieceTable = new PieceTable(text);
            var editor = new EditorControl(pieceTable);
            editor.Theme = ThemeManager.Instance.CurrentTheme;

            string title = GetString(tabEl, "Title") ?? "Untitled";

            // Apply encoding.
            int codePage = GetInt(tabEl, "EncodingCodePage", 65001);
            bool hasBom = GetBool(tabEl, "HasBom");
            try
            {
                var enc = System.Text.Encoding.GetEncoding(codePage);
                editor.EncodingManager = new EncodingManager(enc, hasBom);
            }
            catch
            {
                editor.EncodingManager = new EncodingManager(new UTF8Encoding(false), false);
            }

            // Apply line ending.
            string? lineEnding = GetString(tabEl, "LineEnding");
            if (!string.IsNullOrEmpty(lineEnding))
                editor.LineEnding = lineEnding;

            // Apply language/lexer (skip if custom profile will be applied on activation).
            string? language = GetString(tabEl, "Language");
            string? customProfile = GetString(tabEl, "CustomProfileName");
            if (string.IsNullOrEmpty(customProfile))
            {
                if (!string.IsNullOrEmpty(language))
                {
                    var lexer = LexerRegistry.Instance.GetLexerById(language);
                    if (lexer is not null)
                        editor.SetLexer(lexer);
                }
                else if (path is not null)
                {
                    string ext = Path.GetExtension(path);
                    var lexer = LexerRegistry.Instance.GetLexerByExtension(ext);
                    if (lexer is not null)
                        editor.SetLexer(lexer);
                }
            }

            // Set file size for the status bar from recovered content.
            var sizeEnc = editor.EncodingManager?.CurrentEncoding
                ?? new UTF8Encoding(false);
            editor.FileSizeBytes = sizeEnc.GetByteCount(text);

            bool wordWrap = GetInt(tabEl, "WordWrap", 0) != 0;

            _form.WireAndAddRecoveredTab(new TabInfo
            {
                Id = tabId,
                Title = title,
                FilePath = (path is not null && File.Exists(path)) ? path : null,
                IsModified = true,
                Editor = editor,
                PendingWordWrap = wordWrap ? true : null,
                PendingCustomProfileName = customProfile,
            },
            caret: GetLong(tabEl, "Caret", 0),
            scroll: GetInt(tabEl, "Scroll", 0),
            zoom: GetInt(tabEl, "Zoom", 0));

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to restore text tab: {ex.Message}");
            return false;
        }
    }

    private bool RestorePiecesTab(JsonElement tabEl, Guid tabId, string contentPath, string? path)
    {
        if (path is null || !File.Exists(path))
            return false;

        try
        {
            // Validate original file hasn't changed.
            var fi = new FileInfo(path);
            long savedSize = GetLong(tabEl, "OriginalFileSize", -1);
            string? savedWrite = GetString(tabEl, "OriginalLastWrite");

            if (savedSize >= 0 && fi.Length != savedSize)
            {
                _form.OpenFile(path);
                return true;
            }

            if (savedWrite is not null && DateTime.TryParse(savedWrite, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var savedDt))
            {
                if (Math.Abs((fi.LastWriteTimeUtc - savedDt).TotalSeconds) > 2)
                {
                    _form.OpenFile(path);
                    return true;
                }
            }

            // Read the binary recovery file (tiny — just add buffer + piece descriptors).
            var (addBuffer, pieces) = ReadPiecesFile(contentPath);
            if (pieces is null)
            {
                _form.OpenFile(path);
                return true;
            }

            // Collect metadata before kicking off async work.
            int codePage = GetInt(tabEl, "EncodingCodePage", 65001);
            bool hasBom = GetBool(tabEl, "HasBom");
            string? lineEnding = GetString(tabEl, "LineEnding");
            string? language = GetString(tabEl, "Language");
            string? customProfile = GetString(tabEl, "CustomProfileName");
            long caret = GetLong(tabEl, "Caret", 0);
            int scroll = GetInt(tabEl, "Scroll", 0);
            int zoom = GetInt(tabEl, "Zoom", 0);
            bool wordWrap = GetInt(tabEl, "WordWrap", 0) != 0;

            // Delegate to MainForm's async large-file recovery (same pattern as OpenLargeFile).
            _form.RestoreLargeFileFromRecovery(
                path, fi.Length, addBuffer!, pieces,
                codePage, hasBom, lineEnding, language,
                caret, scroll, zoom, tabId, wordWrap, customProfile);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to restore pieces tab: {ex.Message}");
            if (File.Exists(path))
                _form.OpenFile(path);
            return true;
        }
    }

    // ── Large-file recovery helpers ─────────────────────────────────

    /// <summary>
    /// Filters recovery pieces to only include content within the scanned range
    /// of the original source.  Add-buffer pieces are always included (in memory).
    /// Original pieces beyond the scanned range are truncated or dropped.
    /// </summary>
    internal static IReadOnlyList<Piece> FilterPiecesToScannedRange(
        IReadOnlyList<Piece> pieces, long scannedCharLen, ITextSource source)
    {
        var safe = new List<Piece>(pieces.Count);
        foreach (var p in pieces)
        {
            if (p.BufferType == BufferType.Add)
            {
                safe.Add(p);
                continue;
            }

            long pieceEnd = p.Start + p.Length;
            if (pieceEnd <= scannedCharLen)
            {
                safe.Add(p);
            }
            else if (p.Start < scannedCharLen)
            {
                long safeLen = scannedCharLen - p.Start;
                int lf = source.CountLineFeeds(p.Start, safeLen);
                safe.Add(new Piece(BufferType.Original, p.Start, safeLen, lf));
            }
            // else: entirely beyond scanned range — skip.
        }
        return safe;
    }

    /// <summary>
    /// Computes line offsets for a recovery document by mapping the source's
    /// pre-computed line offsets through the recovery pieces.  O(pieces + lines)
    /// with no content I/O — just arithmetic and a small add-buffer scan.
    /// </summary>
    internal static long[] ComputeRecoveryLineOffsets(
        long[] sourceLineOffsets, string addBuffer, IReadOnlyList<Piece> safePieces)
    {
        // ── Pass 1: compute exact line count by binary-searching the source ──
        // We don't trust Piece.LineFeeds because the recovery file's values may
        // not perfectly match the current source's line offset array.
        long totalLines = 1; // line 0 always exists
        foreach (var piece in safePieces)
        {
            if (piece.BufferType == BufferType.Add)
            {
                int start = (int)piece.Start;
                int end = start + (int)piece.Length;
                for (int i = start; i < end; i++)
                {
                    if (addBuffer[i] == '\n')
                        totalLines++;
                }
            }
            else
            {
                long pieceEnd = piece.Start + piece.Length;
                int lo = LowerBound(sourceLineOffsets, piece.Start + 1);
                int hi = UpperBound(sourceLineOffsets, pieceEnd);
                totalLines += hi - lo;
            }
        }

        // ── Pass 2: allocate and fill ──
        var offsets = new long[totalLines];
        offsets[0] = 0;
        int writeIdx = 1;
        long docOffset = 0;

        foreach (var piece in safePieces)
        {
            if (piece.BufferType == BufferType.Add)
            {
                int start = (int)piece.Start;
                int end = start + (int)piece.Length;
                for (int i = start; i < end; i++)
                {
                    if (addBuffer[i] == '\n')
                        offsets[writeIdx++] = docOffset + (i - start) + 1;
                }
            }
            else
            {
                long pieceEnd = piece.Start + piece.Length;
                int lo = LowerBound(sourceLineOffsets, piece.Start + 1);
                int hi = UpperBound(sourceLineOffsets, pieceEnd);
                int count = hi - lo;

                if (count > 0)
                {
                    long delta = docOffset - piece.Start;
                    Array.Copy(sourceLineOffsets, lo, offsets, writeIdx, count);
                    if (delta != 0)
                    {
                        int end = writeIdx + count;
                        for (int i = writeIdx; i < end; i++)
                            offsets[i] += delta;
                    }
                    writeIdx += count;
                }
            }

            docOffset += piece.Length;
        }

        return offsets;
    }

    /// <summary>
    /// Same as <see cref="ComputeRecoveryLineOffsets"/> but reuses a caller-
    /// provided buffer to avoid per-batch LOH allocations during incremental
    /// loading.  The buffer is grown (with doubling) when needed.
    /// Returns the (possibly reallocated) buffer and the valid entry count.
    /// </summary>
    internal static (long[] Buffer, int Count) ComputeRecoveryLineOffsetsInto(
        long[] sourceLineOffsets, string addBuffer, IReadOnlyList<Piece> safePieces,
        long[]? buffer)
    {
        // ── Pass 1: compute exact line count ──
        long totalLines = 1;
        foreach (var piece in safePieces)
        {
            if (piece.BufferType == BufferType.Add)
            {
                int start = (int)piece.Start;
                int end = start + (int)piece.Length;
                for (int i = start; i < end; i++)
                {
                    if (addBuffer[i] == '\n')
                        totalLines++;
                }
            }
            else
            {
                long pieceEnd = piece.Start + piece.Length;
                int lo = LowerBound(sourceLineOffsets, piece.Start + 1);
                int hi = UpperBound(sourceLineOffsets, pieceEnd);
                totalLines += hi - lo;
            }
        }

        // ── Ensure buffer capacity (doubling strategy) ──
        int needed = (int)totalLines;
        if (buffer is null || buffer.Length < needed)
        {
            int newLen = buffer is null
                ? Math.Max(needed, 1024)
                : Math.Max(needed, buffer.Length * 2);
            buffer = new long[newLen];
        }

        // ── Pass 2: fill ──
        buffer[0] = 0;
        int writeIdx = 1;
        long docOffset = 0;

        foreach (var piece in safePieces)
        {
            if (piece.BufferType == BufferType.Add)
            {
                int start = (int)piece.Start;
                int end = start + (int)piece.Length;
                for (int i = start; i < end; i++)
                {
                    if (addBuffer[i] == '\n')
                        buffer[writeIdx++] = docOffset + (i - start) + 1;
                }
            }
            else
            {
                long pieceEnd = piece.Start + piece.Length;
                int lo = LowerBound(sourceLineOffsets, piece.Start + 1);
                int hi = UpperBound(sourceLineOffsets, pieceEnd);
                int count = hi - lo;

                if (count > 0)
                {
                    long delta = docOffset - piece.Start;
                    Array.Copy(sourceLineOffsets, lo, buffer, writeIdx, count);
                    if (delta != 0)
                    {
                        int end = writeIdx + count;
                        for (int i = writeIdx; i < end; i++)
                            buffer[i] += delta;
                    }
                    writeIdx += count;
                }
            }

            docOffset += piece.Length;
        }

        return (buffer, needed);
    }

    /// <summary>First index where arr[index] >= value.</summary>
    private static int LowerBound(long[] arr, long value)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (arr[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>First index where arr[index] > value.</summary>
    private static int UpperBound(long[] arr, long value)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (arr[mid] <= value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static (string? AddBuffer, IReadOnlyList<Piece>? Pieces) ReadPiecesFile(string contentPath)
    {
        try
        {
            using var fs = new FileStream(contentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // Validate magic.
            byte[] magic = br.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1]
                || magic[2] != Magic[2] || magic[3] != Magic[3])
                return (null, null);

            uint version = br.ReadUInt32();
            if (version != FormatVersion)
                return (null, null);

            long addBufferLen = br.ReadInt64();
            if (addBufferLen < 0 || addBufferLen > 1_000_000_000)
                return (null, null);

            byte[] addBufferBytes = br.ReadBytes((int)addBufferLen);
            string addBuffer = Encoding.UTF8.GetString(addBufferBytes);

            int pieceCount = br.ReadInt32();
            if (pieceCount < 0 || pieceCount > 100_000_000)
                return (null, null);

            var pieces = new List<Piece>(pieceCount);
            for (int i = 0; i < pieceCount; i++)
            {
                var bufType = (BufferType)br.ReadByte();
                long start = br.ReadInt64();
                long length = br.ReadInt64();
                int lineFeeds = br.ReadInt32();
                pieces.Add(new Piece(bufType, start, length, lineFeeds));
            }

            return (addBuffer, pieces);
        }
        catch
        {
            return (null, null);
        }
    }

    // ── Window geometry restore ──────────────────────────────────────

    private void RestoreWindowGeometry(JsonElement root)
    {
        int x = GetInt(root, "WindowX", int.MinValue);
        int y = GetInt(root, "WindowY", int.MinValue);
        int w = GetInt(root, "WindowWidth", 0);
        int h = GetInt(root, "WindowHeight", 0);
        bool maximized = GetInt(root, "WindowMaximized", 0) != 0;

        if (w <= 0 || h <= 0) return;

        var savedRect = new System.Drawing.Rectangle(x, y, w, h);
        bool onScreen = false;
        foreach (var screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(savedRect))
            {
                onScreen = true;
                break;
            }
        }
        if (!onScreen) return;

        _form.StartPosition = FormStartPosition.Manual;
        _form.Location = new System.Drawing.Point(x, y);
        _form.Size = new System.Drawing.Size(w, h);
        if (maximized)
            _form.WindowState = FormWindowState.Maximized;
    }

    private static void RestoreSearchHistory(JsonElement root)
    {
        if (!root.TryGetProperty("SearchHistory", out var historyEl)) return;
        if (historyEl.ValueKind != JsonValueKind.Array) return;

        var items = new List<string>();
        foreach (var item in historyEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                items.Add(item.GetString()!);
        }
        if (items.Count > 0)
            Bascanka.Editor.Panels.FindReplacePanel.SetSearchHistory(items);
    }

    // ── JSON helpers ─────────────────────────────────────────────────

    private static int GetInt(JsonElement el, string name, int defaultValue = 0)
    {
        if (el.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int val))
                return val;
        }
        return defaultValue;
    }

    private static long GetLong(JsonElement el, string name, long defaultValue = 0)
    {
        if (el.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out long val))
                return val;
        }
        return defaultValue;
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static bool GetBool(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }

    // ── IDisposable ──────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
