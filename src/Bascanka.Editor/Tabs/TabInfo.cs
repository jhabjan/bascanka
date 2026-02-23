using Bascanka.Editor.Controls;

namespace Bascanka.Editor.Tabs;

/// <summary>
/// Holds the metadata and editor reference for a single open tab in the editor.
/// Each tab is uniquely identified by its <see cref="Id"/> and tracks whether
/// its content has been modified since the last save.
/// </summary>
public sealed class TabInfo
{
    /// <summary>
    /// Unique, immutable identifier for this tab instance.  Generated once at
    /// construction and never changes, even if the tab is reordered or renamed.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Display title shown on the tab strip.  Typically the file name (without
    /// path) for file-backed tabs, or a placeholder such as "Untitled 1" for
    /// new, unsaved documents.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the file on disk, or <see langword="null"/> if the document
    /// has never been saved.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Indicates whether the document content has been modified since its last
    /// save.  When <see langword="true"/>, the tab strip renders a modified
    /// indicator (e.g. a dot or asterisk) alongside the title.
    /// </summary>
    public bool IsModified { get; set; }

    /// <summary>
    /// The <see cref="EditorControl"/> that provides the editing surface for
    /// this tab's document.
    /// </summary>
    public required EditorControl Editor { get; set; }

    /// <summary>
    /// Indicates that this tab is showing a binary file in hex-only mode.
    /// Binary tabs cannot be saved as text.
    /// </summary>
    public bool IsBinaryMode { get; set; }

    /// <summary>
    /// Arbitrary user data associated with this tab.  Consumers may use this
    /// property to attach additional context (plugin state, document metadata,
    /// etc.) without subclassing <see cref="TabInfo"/>.
    /// </summary>
    public object? Tag { get; set; }

    /// <summary>
    /// When true, the file has not been loaded yet. The document will be loaded
    /// from <see cref="FilePath"/> when the tab is first activated.
    /// </summary>
    public bool IsDeferredLoad { get; set; }

    /// <summary>Pending zoom level to apply after loading/activation.</summary>
    public int PendingZoom { get; set; }

    /// <summary>Pending scroll position to apply after loading/activation.</summary>
    public int PendingScroll { get; set; }

    /// <summary>Pending caret offset to apply after loading/activation.</summary>
    public long PendingCaret { get; set; }

    /// <summary>Pending word-wrap state to apply after loading/activation.</summary>
    public bool? PendingWordWrap { get; set; }

    /// <summary>Pending language ID to apply after loading/activation (overrides extension detection).</summary>
    public string? PendingLanguage { get; set; }

    /// <summary>Pending custom highlight profile name to apply after loading/activation.</summary>
    public string? PendingCustomProfileName { get; set; }

    /// <summary>
    /// Last selected custom highlight profile for this tab. Used for session
    /// persistence even if temporary lexer switches occur before shutdown.
    /// </summary>
    public string? SelectedCustomProfileName { get; set; }

    /// <summary>
    /// When set (e.g. "pieces"), indicates this deferred tab has modified content
    /// stored in the recovery directory.  Used by the recovery manifest writer to
    /// preserve the format across re-saves, so the data survives even if the tab
    /// is never activated.
    /// </summary>
    public string? PendingRecoveryFormat { get; set; }

    /// <summary>Encoding code page to persist for deferred recovery tabs.</summary>
    public int PendingEncodingCodePage { get; set; }

    /// <summary>Whether the file had a BOM (for deferred recovery tabs).</summary>
    public bool PendingHasBom { get; set; }

    /// <summary>Line ending style to persist for deferred recovery tabs.</summary>
    public string? PendingLineEnding { get; set; }

    /// <summary>
    /// True while the tab's file is being loaded asynchronously (e.g. large file
    /// incremental scan or recovery loading).  Saving is blocked while loading.
    /// </summary>
    public bool IsLoading { get; set; }

    /// <summary>
    /// Returns the display title, including a modified indicator when applicable.
    /// </summary>
    public string DisplayTitle => IsModified ? $"* {Title}" : Title;

    /// <inheritdoc/>
    public override string ToString() => DisplayTitle;
}
