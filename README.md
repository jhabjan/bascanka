# Bascanka

<p align="center">
  <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/bascanka_small.png"
       alt="Bascanka screenshot"
       width="35%">
</p>

**Bascanka** is a free and open-source large file text editor for Windows designed as a modern, lightweight alternative to traditional editors. It supports a wide range of programming and markup languages and is distributed under the GNU General Public License Version 3.

UI and text rendering engine are built entirely from scratch in **C#** on **.NET 10**, Bascanka is engineered for performance, portability, and simplicity. It runs as a single self-contained executable with no third-party dependencies - just copy and run. Its architecture is optimized for responsiveness even when working with extremely large files, including datasets and logs in the multi-gigabyte range (**10 GB and beyond**).

Bascanka focuses on efficient resource usage and fast text processing while maintaining a clean, practical editing experience. By minimizing overhead and avoiding unnecessary dependencies, it delivers high performance with a small footprint - making it suitable for both everyday editing and demanding large-file workloads.

## Downloads

### 📦 Bascanka v.1.0.2

#### Release notes
- added CJK / Unicode Character Support - Fixed cursor positioning for CJK (Chinese, Japanese, Korean) and other fullwidth characters. The caret, selection highlights, search highlights, and all   other visual elements now align correctly with double-width characters.
- added GB2312 encoding and support for detecting GB2312 when opening files.
- added the ability to compare opened tabs text via the tab right-click context menu.
- added a per-tab progress bar when saving large files, allowing users to continue working on other tabs while a file is being saved.
- added system theme detection feature.
- added Chinese UI localization.
- added Serbian (Cyrillic) UI localization.
- fixed arrow key navigation to skip over surrogate pairs as a single unit instead of requiring two key presses per supplementary character.
- fixed box/column selection losing column mode when starting with Delete or Backspace, so it now stays in column mode for continued editing.
- fixed performance issue when editing large files and jumping + making changes between beginning and end of the file.
- extended the About window to include contributors and release notes.
  
---

- **Framework-dependent (small download - requires .NET 10 runtime)**  
  Single portable EXE (~2 MB). Use this if .NET 10 is already installed on your system.  
  👉 https://beegoesmoo.co.uk/bascanka/download/Bascanka.v.1.0.2.bin.zip  
  **SHA256:** `8C736BB162E4F1D6491B44163D4E32175975ACDB75F12E0CC6FF790C1D5F02EC`

- **Self-contained (no runtime required)**  
  Single portable EXE with .NET 10 included (~120 MB). Works on any supported Windows machine without installing .NET.  
  👉 https://beegoesmoo.co.uk/bascanka/download/Bascanka.v.1.0.2.bin.sc.zip  
  **SHA256:** `77C4A1873555EF02CA9CC8B1FF21D3D0DE5EC398FC678BD32BA8DFC164703010`

All builds are portable - no installation required.

### 📦 Bascanka v.1.0.1

#### Release notes
- added Side-by-side file comparison. Available from Tools > Compare Files.
- added Sed Transform / Unix sed-style substitution with live preview. Available from Tools > Sed Transform.
- added Custom Highlighting + Folding / User-defined regex-based highlighting + folding profiles
- added COBOL Lexer / language support.
- added persistence for window position, size, maximized state and opened tabs.
- extended to be fully customizable via Tools > Settings (theme colors, fonts, text rendering engine limits, etc).
- introduced a --reset (-r) command-line parameter for bascanka.exe, allowing users to clear the session state and start with a fresh instance
- other small bug fixes

All builds are portable - no installation required.

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_main_2.png" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_main_2.png"
         width="100%">
  </a>
</p>

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/cust_high_demo_1.gif" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/cust_high_demo_1.gif"
         width="100%">
  </a>
</p>

## Screenshots

#### Text editor and Hex Editor

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_main.png" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_main.png"
         width="100%">
  </a>
</p>

#### Syntax highlighting

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_js.png" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_js.png"
         width="100%">
  </a>
</p>

#### Hex editor

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_hex.png" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_hex.png"
         width="100%">
  </a>
</p>

#### Custom Highlighting

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/custom_highlighting.png" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/custom_highlighting.png"
         width="100%">
  </a>
</p>

## Features

- Supports large text files (10 GB+)
- Syntax highlighting for common languages (C#, JavaScript, Python, HTML, CSS, JSON, XML, and more)
- Hex editor
- Find & replace with regex support
- Column (Box) Selection Mode
- Macro recording and playback
- Tab-based editing
- Word wrap
- Zoom in / zoom out
- Multilanguage UI (English and Croatian built-in, extensible via JSON)
- Theming support

## Build as single exe

#### Self-contained single EXE (includes .NET 10 runtime / ~120 MB exe)
```
dotnet publish "src\Bascanka.App\Bascanka.App.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
```
#### Framework-dependent single EXE (requires .NET 10 runtime installed / ~2 MB exe)
```
dotnet publish "src\Bascanka.App\Bascanka.App.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=false
```

## Run

```
dotnet run --project src/Bascanka.App/Bascanka.App.csproj
```

## Project Structure

```
src/
  Bascanka.Core/          # Text buffer (piece table), search engine, commands
  Bascanka.Editor/        # Editor controls, gutter, tabs, panels, themes
  Bascanka.Plugins.Api/   # Plugin interfaces
  Bascanka.App/           # WinForms application, menus, localization
```

## About the Name

The name "Bascanka" comes from the [Bašćanska ploča](https://en.wikipedia.org/wiki/Ba%C5%A1%C4%87anska_plo%C4%8Da) (Baska tablet) - a stone tablet from around 1100 AD, found in the Church of St. Lucy near Baska on the island of Krk, Croatia. It is one of the oldest known inscriptions in the Croatian language, written in Glagolitic script. The tablet documents a royal land donation by King Zvonimir and is a cornerstone of Croatian cultural heritage and literacy.

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/bascanska_ploca.jpg" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/bascanska_ploca.jpg"
         alt="Bascanka main screen"
         width="50%">
  </a>
</p>

## Author

Josip Habjan (habjan@gmail.com)

## License

GNU General Public License Version 3

---

# MANUAL

## Table of Contents

- [File Operations](#file-operations)
- [Tab Management](#tab-management)
- [Editing](#editing)
- [Selection Modes](#selection-modes)
- [Undo / Redo](#undo--redo)
- [Search & Replace](#search--replace)
- [Sed Transform](#sed-transform)
- [Text Transformations](#text-transformations)
- [Syntax Highlighting](#syntax-highlighting)
- [Custom Highlighting](#custom-highlighting)
- [Code Folding](#code-folding)
- [Symbol Navigation](#symbol-navigation)
- [Diff / Compare](#diff--compare)
- [Hex Editor](#hex-editor)
- [Macros](#macros)
- [Zoom](#zoom)
- [Word Wrap](#word-wrap)
- [Whitespace Display](#whitespace-display)
- [Full Screen](#full-screen)
- [Printing](#printing)
- [Encoding & Line Endings](#encoding--line-endings)
- [Themes](#themes)
- [Localization](#localization)
- [Settings](#settings)
- [Session Management](#session-management)
- [Large File Handling](#large-file-handling)
- [Plugin System](#plugin-system)
- [Command-Line Interface](#command-line-interface)
- [Explorer Integration](#explorer-integration)
- [Status Bar](#status-bar)
- [Keyboard Shortcuts](#keyboard-shortcuts)

---

## File Operations

### Opening Files

| Action | Shortcut | Description |
|--------|----------|-------------|
| New | `Ctrl+N` | Create an empty untitled document |
| Open | `Ctrl+O` | Open one or more files (multi-select) |
| Open Recent | | MRU list of up to 20 files (configurable) |
| Drag & Drop | | Drop files onto the window to open them |

Files can also be opened from the command line or via single-instance IPC (double-clicking a file in Explorer when Bascanka is already running sends it to the existing instance).

**Large files** (>10 MB by default) load asynchronously in the background using memory-mapped I/O, with a progress overlay and cancel button. The UI stays responsive.

**Binary files** are detected automatically (first 8 KB scanned) and open directly in hex editor mode.

**Admin elevation**: if a file cannot be opened due to permissions, Bascanka offers to restart as Administrator.

### Saving Files

| Action | Shortcut | Description |
|--------|----------|-------------|
| Save | `Ctrl+S` | Save current document (prompts for path if untitled) |
| Save As | `Ctrl+Shift+S` | Save to a new file path |
| Save All | | Save all modified documents |

Original encoding and line ending style are preserved on save.

### File Watcher

Open files are monitored for external changes. When a file is modified outside Bascanka, a prompt offers to reload or keep the local version.

### Recent Files

- Up to 20 most recently used files (configurable: 5-100)
- File > Open Recent submenu
- "Clear Recent Files" option
- Stored in `%APPDATA%\Bascanka\recent.json`

---

## Tab Management

Multiple documents open as tabs in a tab strip across the top of the window.

### Tab Operations

| Action | Shortcut | Description |
|--------|----------|-------------|
| Next Tab | `Ctrl+Tab` | Switch to the next tab |
| Previous Tab | `Ctrl+Shift+Tab` | Switch to the previous tab |
| Close Tab | `Ctrl+W` | Close the current tab (prompts to save if modified) |

### Tab Context Menu (Right-Click)

- **Close** - close this tab
- **Close Others** - close all tabs except this one
- **Close All** - close all tabs
- **Close to Right** - close tabs to the right
- **Copy File Path** - copy full path to clipboard
- **Open in Explorer** - reveal in Windows Explorer

### Tab Features

- Drag and drop to reorder tabs
- Modified indicator (`*` after filename)
- Untitled tabs named "Untitled 1", "Untitled 2", etc.
- Each tab maintains independent zoom, scroll position, caret, selection, undo history, encoding, line ending, language, and custom highlight profile
- **Deferred loading**: on session restore, only the active tab loads immediately; other tabs load when activated

### Tab Appearance (Configurable)

| Setting | Default | Range |
|---------|---------|-------|
| Tab Height | 30 px | 20-60 |
| Max Tab Width | 220 px | 100-500 |
| Min Tab Width | 80 px | 40-200 |

---

## Editing

### Basic Editing

- Character-by-character insert and delete
- Insert/Overwrite mode toggle (`Insert` key)
- Auto-indent on Enter (copies leading whitespace from previous line)
- Tab inserts spaces (width configurable, default 4)

### Line Operations

| Action | Shortcut | Description |
|--------|----------|-------------|
| Duplicate Line | `Ctrl+D` | Duplicate the current line below |
| Delete Line | `Ctrl+L` or `Ctrl+Shift+K` | Delete the entire current line |
| Move Line Up | `Alt+Up` | Swap the current line with the one above |
| Move Line Down | `Alt+Down` | Swap the current line with the one below |
| Copy Line Up | `Ctrl+Shift+Up` | Copy the current line above |
| Copy Line Down | `Ctrl+Shift+Down` | Copy the current line below |
| Indent | `Tab` (with selection) | Indent all selected lines |
| Unindent | `Shift+Tab` | Unindent current line or selected lines |

### Clipboard

| Action | Shortcut | Description |
|--------|----------|-------------|
| Cut | `Ctrl+X` | Cut selection to clipboard |
| Copy | `Ctrl+C` | Copy selection to clipboard |
| Paste | `Ctrl+V` | Paste from clipboard at caret |

Column selection cut/copy produces tab-separated values; column paste inserts into each line of the column.

### Context Menu (Right-Click on Text)

Undo, Redo, Cut, Copy, Paste, Delete, Select All - with enablement based on current state.

---

## Selection Modes

### Standard Selection

- `Shift+Arrow` keys to extend character/line at a time
- `Ctrl+Shift+Arrow` to extend word at a time
- `Shift+Home/End` to extend to line start/end
- `Ctrl+Shift+Home/End` to extend to document start/end
- `Shift+Page Up/Down` to extend by page
- `Ctrl+A` to select all
- Click and drag to select with the mouse
- Double-click to select a word
- Triple-click to select a line

### Column (Box) Selection

- `Alt+Click` and drag to start a rectangular column selection
- `Alt+Shift+Arrow` keys to extend column selection
- Typing inserts on each line of the column
- Copy/paste preserves the rectangular shape

---

## Undo / Redo

| Action | Shortcut |
|--------|----------|
| Undo | `Ctrl+Z` |
| Redo | `Ctrl+Y` |

- Unlimited undo/redo history
- Consecutive character inserts and deletes auto-merge into a single undoable operation
- Composite commands group multi-step operations (e.g. Replace All) into a single undo step
- Save-point tracking drives the modified indicator (`*`) on tabs

---

## Search & Replace

### Find Panel (`Ctrl+F`)

Modeless panel anchored to the top-right of the editor (VS Code style).

| Option | Description |
|--------|-------------|
| Match Case | Case-sensitive search |
| Whole Word | Match whole words only |
| Use Regex | Enable regular expression patterns |
| Wrap Around | Continue search from start/end of document |

### Replace Panel (`Ctrl+H`)

Same as Find, plus:

| Action | Description |
|--------|-------------|
| Replace | Replace current match and advance |
| Replace All | Replace all matches (with count) |

### Bulk Operations

| Action | Description |
|--------|-------------|
| Find All | List all matches in the current document (up to 100,000) |
| Find in Tabs (`Ctrl+Shift+F`) | Search across all open documents |
| Mark All | Highlight all matches in the viewport |
| Count | Count occurrences without navigating |

### Find Results Panel

- Bottom panel with paginated results (1,000 per page)
- Shows file path, line number, column, and matched text with context
- Click a result to jump to it in the editor
- "Open Results in New Tab" button

### Performance

- Hardware-accelerated literal search via vectorized `string.IndexOf` (~10x faster than regex)
- 4 MB sliding-window search for large files
- Background thread with progress indicator and cancel button
- Incremental search as you type (300 ms debounce, configurable)
- Search history: last 25 searches persisted (configurable)

### Go to Line (`Ctrl+G`)

Dialog to jump to a specific line number (1-based).

---

## Sed Transform

Unix `sed`-style substitution with live preview. Available from Tools > Sed Transform.

### Syntax

```
s/pattern/replacement/flags
```

| Part | Description |
|------|-------------|
| `pattern` | Regular expression to match |
| `replacement` | Substitution text (`$1`, `$2` for capture groups) |
| `flags` | `g` = all occurrences, `i` = ignore case |

Any non-alphanumeric character can be used as delimiter instead of `/`.

### Examples

| Expression | Effect |
|------------|--------|
| `s/foo/bar/g` | Replace all "foo" with "bar" |
| `s/error/warning/` | Replace first occurrence only |
| `s/hello/world/gi` | Case-insensitive, all occurrences |
| `s\|C:\\old\|C:\\new\|g` | Use `\|` as delimiter (handy for paths) |
| `s/(\w+)@(\w+)/$2:$1/g` | Swap capture groups: `user@host` becomes `host:user` |
| `s/^\s+//` | Remove leading whitespace |
| `s/TODO(.*)/<b>TODO$1<\/b>/g` | Wrap TODO comments in HTML bold tags |

### Workflow

1. Enter expression in the dialog (real-time syntax colorization)
2. Double-click an example to populate the input
3. Click OK to open a preview tab showing the result
4. Changed text is highlighted in the preview
5. Click **Apply** to commit or **Discard** to cancel

---

## Text Transformations

Available from the Text menu. Most require a selection; JSON operations work on selection or entire document.

### Case Conversion

| Transformation | Example |
|----------------|---------|
| UPPERCASE | `hello world` -> `HELLO WORLD` |
| lowercase | `Hello World` -> `hello world` |
| Title Case | `hello world` -> `Hello World` |
| sWAP cASE | `Hello World` -> `hELLO wORLD` |

### Encoding / Decoding

| Transformation | Example |
|----------------|---------|
| Base64 Encode | `Hello` -> `SGVsbG8=` |
| Base64 Decode | `SGVsbG8=` -> `Hello` |
| URL Encode | `hello world` -> `hello%20world` |
| URL Decode | `hello%20world` -> `hello world` |
| HTML Encode | `<div>` -> `&lt;div&gt;` |
| HTML Decode | `&lt;div&gt;` -> `<div>` |

### Line Operations

| Transformation | Description |
|----------------|-------------|
| Sort Lines Ascending | Alphabetical A-Z |
| Sort Lines Descending | Alphabetical Z-A |
| Remove Duplicate Lines | Keep first occurrence of each unique line |
| Reverse Lines | Reverse the order of selected lines |

### Whitespace

| Transformation | Description |
|----------------|-------------|
| Trim Trailing Whitespace | Remove spaces/tabs from end of each line |
| Trim Leading Whitespace | Remove spaces/tabs from start of each line |
| Compact Whitespace | Collapse multiple spaces/tabs to a single space |
| Tabs to Spaces | Convert tabs to 4 spaces |
| Spaces to Tabs | Convert 4 spaces to tabs |

### Other

| Transformation | Description |
|----------------|-------------|
| Reverse Text | Grapheme-cluster-aware reversal (handles emoji, surrogate pairs) |
| Format JSON | Pretty-print JSON with tab indentation |
| Minimize JSON | Compact single-line JSON |

---

## Syntax Highlighting

19 built-in languages with automatic detection by file extension.

| Language | Extensions |
|----------|------------|
| C# | `.cs` |
| JavaScript | `.js`, `.mjs`, `.cjs` |
| TypeScript | `.ts`, `.tsx` |
| Python | `.py` |
| HTML | `.html`, `.htm` |
| CSS | `.css` |
| XML | `.xml`, `.xaml`, `.csproj`, `.svg` |
| JSON | `.json` |
| SQL | `.sql` |
| COBOL | `.cbl`, `.cob`, `.cpy`, `.cobol` |
| Bash / Shell | `.sh`, `.bash` |
| C | `.c`, `.h` |
| C++ | `.cpp`, `.hpp`, `.cc`, `.cxx` |
| Java | `.java` |
| PHP | `.php` |
| Ruby | `.rb` |
| Go | `.go` |
| Rust | `.rs` |
| Markdown | `.md`, `.markdown` |

### Features

- Automatic language detection by file extension
- Manual language selection via Language menu or status bar click
- "Plain Text" mode disables highlighting
- Stateless per-line lexing with token cache and edit-based invalidation
- Token types: Keyword, String, Number, Comment, Operator, Identifier, Type, Function, Preprocessor, and more

---

## Custom Highlighting

User-defined regex-based highlighting profiles, stored in `Bascanka.json` next to the executable.

### JSON Format

```json
{
  "custom_highlighting": [
    {
      "name": "Log File",
      "rules": [
        {
          "pattern": "ERROR.*",
          "scope": "match",
          "foreground": "#FF4444",
          "background": "#3C1010"
        },
        {
          "pattern": "WARN.*",
          "scope": "match",
          "foreground": "#FFAA00"
        },
        {
          "scope": "block",
          "begin": "<request>",
          "end": "</request>",
          "foldable": true,
          "foreground": "#66CCFF",
          "background": "#0A1520"
        }
      ]
    }
  ]
}
```

### Rule Types

| Type | Fields | Description |
|------|--------|-------------|
| match | `pattern`, `foreground`, `background` | Highlight regex matches on exact pattern |
| line | `pattern`, `foreground`, `background` | Highlight regex matches on complete line |
| block | `begin`, `end`, `foldable`, `foreground`, `background` | Highlight multi-line regions between start/end patterns |

### Management

- Language > Custom Highlighting submenu lists all profiles
- Language > Manage Custom Highlighting opens a dialog to add, edit, delete, and reorder profiles

---

## Code Folding

### Operations

| Action | Shortcut | Description |
|--------|----------|-------------|
| Toggle Fold | `Ctrl+Shift+[` | Fold or unfold the region at the caret |
| Fold All | `Ctrl+Shift+-` | Collapse all foldable regions |
| Unfold All | `Ctrl+Shift++` | Expand all collapsed regions |

### Features

- Brace-based fold detection for `{}`, `[]`, `()`
- Language-specific folding rules
- Custom highlighting profiles can define foldable blocks (`"foldable": true`)
- Clickable `[+]` / `[-]` markers in the gutter
- Collapsed regions show `...` indicator after the fold start line
- Disabled for files > 50 MB (configurable)

---

## Symbol Navigation

Side panel showing code structure (View > Symbol List).

### Supported Languages

| Language | Symbol Types |
|----------|-------------|
| C# | Classes, interfaces, enums, structs, methods, properties |
| Java | Classes, interfaces, enums, methods |
| JavaScript / TypeScript | Classes, functions, methods |
| Python | Classes, functions, methods |
| Go | Structs, interfaces, functions |
| Rust | Structs, enums, functions, impl blocks |
| C / C++ | Structs, functions |

Click a symbol to jump to its definition. The list refreshes automatically when switching documents.

---

## Diff / Compare

Side-by-side file comparison. Available from Tools > Compare Files.

### Compare Sources

- Current file vs. another open tab
- Current file vs. an external file (file picker)

### Color Coding

| Color | Meaning |
|-------|---------|
| Green | Added lines |
| Purple | Removed lines |
| Blue | Modified lines (with character-level diff highlighting) |
| Gray | Padding (blank lines inserted for alignment) |

### Features

- Line-level diff using LCS (Longest Common Subsequence) algorithm
- Character-level diff within modified lines
- Synchronized scrolling between left and right panes
- Colored gutter markers for changed sections

---

## Hex Editor

Toggle with Tools > Hex Editor or automatic for binary files.

### Layout

```
Offset     00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F   ASCII
00000000   48 65 6C 6C 6F 20 57 6F  72 6C 64 0A 00 00 00 00   Hello World.....
```

- **Offset column**: byte offset in hex
- **Hex bytes**: 16 bytes per row, grouped in 8-byte halves
- **ASCII column**: printable characters (`.` for non-printable)

### Data Inspector

Select bytes to see them interpreted as:

| Type | Endianness |
|------|------------|
| Int8 / UInt8 | N/A |
| Int16 / UInt16 | Little & Big |
| Int32 / UInt32 | Little & Big |
| Int64 / UInt64 | Little & Big |
| Float / Double | Little & Big |
| UTF-8 String | N/A |

### Synchronization

- Hex view auto-updates when text editor content changes
- Scrolls in sync with the text editor

---

## Macros

Record and replay sequences of editing actions.

### Recording

| Action | Menu |
|--------|------|
| Start Recording | Tools > Record Macro |
| Stop Recording | Tools > Stop Recording |

Status bar shows a red "REC" indicator during recording.

### Captured Actions

- Character input (typing)
- Deletions (Backspace, Delete)
- Caret movements (arrow keys, Home, End, Page Up/Down)
- Consecutive identical actions auto-merge for compact storage

### Playback

| Action | Shortcut |
|--------|----------|
| Play Macro | `F5` |

Replays the last recorded macro. Fully supports undo (entire playback is one undo step).

### Macro Manager

Tools > Macro Manager dialog to:

- View all saved macros
- Replay a saved macro
- Delete macros

Macros are stored as JSON in `%APPDATA%\Bascanka\macros\`.

---

## Zoom

| Action | Shortcut | Description |
|--------|----------|-------------|
| Zoom In | `Ctrl++` or `Ctrl+Mouse Wheel Up` | Increase font size by 1 px |
| Zoom Out | `Ctrl+-` or `Ctrl+Mouse Wheel Down` | Decrease font size by 1 px |
| Reset Zoom | `Ctrl+0` | Restore default font size |

- Minimum font size: 6 px (configurable)
- Zoom percentage shown in status bar (e.g. 100%, 150%, 200%)
- Each tab maintains independent zoom level, persisted across sessions

---

## Word Wrap

Toggle from View > Word Wrap.

- Long lines wrap at the viewport width
- Line numbers in the gutter show logical (document) line numbers
- Caret and arrow keys navigate visual rows
- Scrollbar reflects total visual rows

---

## Whitespace Display

Toggle from View > Show Whitespace.

| Character | Glyph |
|-----------|-------|
| Space | `·` (middle dot) |
| Tab | `→` (right arrow) |
| Newline | `¶` (pilcrow) at end of line |

Drawn as an overlay with configurable opacity (10-255, default 100).

---

## Full Screen

`F11` toggles full-screen mode: hides the menu bar, status bar, and window border, and maximizes the window. Press `F11` again to restore.

---

## Printing

| Action | Shortcut | Description |
|--------|----------|-------------|
| Print | `Ctrl+P` | System print dialog |
| Print Preview | | Preview before printing |

Prints document text with the current editor font. Document title is shown as a header on the first page. Automatic page breaks respect line boundaries.

---

## Encoding & Line Endings

### Supported Encodings

| Encoding | BOM Detection |
|----------|---------------|
| UTF-8 | Optional BOM (`EF BB BF`) |
| UTF-8 with BOM | Forced BOM |
| UTF-16 LE | BOM `FF FE` |
| UTF-16 BE | BOM `FE FF` |
| ASCII | All bytes < 128 |
| Windows-1252 | Fallback heuristic |
| ISO-8859-1 | Explicit selection |

Encoding is auto-detected on open and preserved on save. Change encoding via the Encoding menu or by clicking the encoding label in the status bar.

### Line Endings

| Style | Bytes | Platform |
|-------|-------|----------|
| CRLF | `\r\n` | Windows |
| LF | `\n` | Unix / macOS |
| CR | `\r` | Classic Mac |

Line ending style is auto-detected on open. Internally all line endings are normalized to `\n`; the original style is restored on save. Change via the Encoding menu or by clicking the line-ending label in the status bar.

---

## Themes

Two built-in themes: **Dark** (default) and **Light**.

### Customizable Colors (30+)

| Category | Properties |
|----------|------------|
| Editor | Background, Foreground, Line Highlight, Selection BG/FG, Caret, Bracket Match, Match Highlight |
| Gutter | Background, Foreground, Current Line, Folding Marker |
| Tabs | Bar BG, Active BG/FG, Inactive BG/FG, Border |
| Status Bar | Background, Foreground |
| Find Panel | Background, Foreground |
| Menus | Background, Foreground, Highlight |
| Scroll Bar | Background, Thumb |
| Diff | Added BG, Removed BG, Modified BG, Modified Char BG, Padding BG |
| Syntax Tokens | Keyword, String, Number, Comment, Operator, Identifier, Type, Function, Preprocessor |

### Color Customization (Settings > Appearance)

- Click any color swatch to open a color picker
- Hex display (`#RRGGBB`)
- Reset individual colors or all colors to theme defaults
- Per-theme overrides stored separately
- Export/import themes as JSON

---

## Localization

### Built-in Languages

| Code | Name |
|------|------|
| `en` | English |
| `hr` | Hrvatski (Croatian) |
| `sr` | Ћирилица (Serbian Cyrillic) |

### Switching Language

Settings > Appearance > UI Language. The entire UI (menus, dialogs, status bar, error messages) rebuilds in the selected language.

### Adding Languages

Drop a new `lang_xx.json` file in the Resources folder. Structure:

```json
{
  "languageName": "Display Name",
  "languageCode": "xx",
  "strings": {
    "MenuFile": "File",
    "MenuEdit": "Edit",
    ...
  }
}
```

---

## Settings

Open from Tools > Settings. Stored in the Windows Registry under `HKCU\Software\Bascanka`.

### Editor

| Setting | Default | Range |
|---------|---------|-------|
| Font Family | Consolas | Any installed monospace font |
| Font Size | 11 | 6-72 |
| Tab Width | 4 | 1-16 |
| Auto Indent | On | Boolean |
| Scroll Speed (lines per notch) | 3 | 1-20 |
| Caret Scroll Buffer | 4 | 0-20 |

### Appearance

| Setting | Default |
|---------|---------|
| Theme | Dark |
| UI Language | English |
| Color Customization | 30+ individual color overrides |

### Display

| Setting | Default | Range |
|---------|---------|-------|
| Caret Blink Rate (ms) | 500 | 100-2000 |
| Text Left Padding (px) | 6 | 0-40 |
| Line Spacing (px) | 2 | 0-20 |
| Min Zoom Font Size | 6 | 2-20 |
| Whitespace Opacity | 100 | 10-255 |
| Fold Indicator Opacity | 60 | 10-255 |
| Gutter Padding Left/Right | 8 / 12 | 0-30 |
| Fold Button Size | 10 | 6-24 |
| Bookmark Size | 8 | 4-20 |
| Tab Height / Max Width / Min Width | 30 / 220 / 80 | see ranges above |

### Performance

| Setting | Default | Range |
|---------|---------|-------|
| Large File Threshold (MB) | 10 | 1-1000 |
| Folding Max File Size (MB) | 50 | 1-500 |
| Max Recent Files | 20 | 5-100 |
| Search History Limit | 25 | 5-100 |
| Search Debounce (ms) | 300 | 50-2000 |

### System

- **Explorer Context Menu** - register/unregister "Edit with Bascanka" in right-click menu
- **Export Settings** - save all settings to a JSON file
- **Import Settings** - load settings from a JSON file
- **Reset to Defaults** - restore factory settings (preserves session state)

---

## Session Management

Bascanka automatically saves and restores your workspace.

### What Is Persisted

- Window position, size, and maximized state
- All open tabs (file paths)
- Active (selected) tab
- Per-tab state: zoom, scroll position, caret position

### Deferred Loading

On startup, only the active tab loads immediately. Other tabs load on demand when you click them, keeping startup fast even with many open files.

### Storage

Windows Registry: `HKCU\Software\Bascanka\Session`

### Reset

Use `--reset` or `-r` on the command line to clear session state and start fresh.

---

## Large File Handling

Files above the configurable threshold (default 10 MB) use a specialized pipeline.

| Component | Purpose |
|-----------|---------|
| `MemoryMappedFileSource` | Maps file into virtual memory; OS pages data in/out as needed |
| Chunk Directory | O(log N) random access via binary search over `_chunkCharOffsets[]` |
| Pre-computed Line Offsets | Built during background scan; avoids O(N^2) full-file scan |
| `ChunkCache` (4 MB) | Bounds memory for decoded text from mapped files |
| Background Loading | Async with progress overlay and cancel button |

### Text Buffer Performance

| Operation | Complexity |
|-----------|------------|
| Insert / Delete | O(log N) |
| Character lookup | O(log N) |
| Line lookup | O(log N) |
| Undo / Redo | O(1) |
| Literal search | O(N) with vectorized acceleration |
| Regex search | O(N * pattern) |

---

## Plugin System

Plugins extend Bascanka with custom functionality. Enabled via the `--plugins` command-line flag.

### Plugin Interface

```csharp
public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    void Initialize(IEditorHost host);
    void Shutdown();
}
```

### Available APIs

| API | Capabilities |
|-----|-------------|
| `IBufferApi` | Read/write document text, get line count, get selection, undo/redo |
| `IMenuApi` | Add/remove menu items |
| `IPanelApi` | Add/remove side and bottom panels |
| `IStatusBarApi` | Add/remove status bar fields |

### Loading

- Place `.dll` or `.cs` files in a `Plugins/` folder next to the executable
- C# script files are compiled at runtime via `ScriptCompiler`
- Plugins menu appears in the menu bar with dynamic entries per loaded plugin

---

## Command-Line Interface

```
Bascanka.exe [options] [file1 file2 ...]
```

| Argument | Description |
|----------|-------------|
| `file1 file2 ...` | Open the specified files in tabs |
| `--plugins` | Enable the Plugins menu |
| `-r`, `--reset` | Clear session state and start fresh |

### Single-Instance Behavior

When files are passed on the command line and Bascanka is already running, the files are sent to the existing instance via IPC. No-argument launches always start a new instance.

---

## Explorer Integration

Settings > System > "Edit with Bascanka" adds a context menu entry to Windows Explorer for all file types.

- Registry path: `HKCU\Software\Classes\*\shell\Bascanka`
- No admin rights required (uses `HKEY_CURRENT_USER`)
- Register or unregister with a single checkbox

---

## Status Bar

Left-to-right labels (all proportionally scaled with window width):

| Label | Content | Clickable |
|-------|---------|-----------|
| Position | `Ln 42 : Col 17` | No |
| Selection | `Sel: 128` (when text is selected) | No |
| Encoding | `UTF-8`, `UTF-16 LE`, etc. | Yes (quick menu) |
| Line Ending | `CRLF`, `LF`, `CR` | Yes (quick menu) |
| Language | `C#`, `JSON`, `Plain Text`, etc. | Yes (quick menu) |
| File Size | `1.2 KB`, `45.3 MB`, etc. | No |
| Insert Mode | `INS` or `OVR` | No |
| Read-Only | `R/O` (when file is read-only) | No |
| Zoom | `100%`, `150%`, etc. | No |
| Macro | Red `REC` badge (during recording) | No |

---

## Keyboard Shortcuts

All shortcuts are customizable. Overrides are stored in `%APPDATA%\Bascanka\shortcuts.json`.

### File

| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | New |
| `Ctrl+O` | Open |
| `Ctrl+S` | Save |
| `Ctrl+Shift+S` | Save As |
| `Ctrl+P` | Print |
| `Ctrl+W` | Close Tab |
| `Alt+F4` | Exit |

### Edit

| Shortcut | Action |
|----------|--------|
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Ctrl+X` | Cut |
| `Ctrl+C` | Copy |
| `Ctrl+V` | Paste |
| `Ctrl+A` | Select All |
| `Ctrl+D` | Duplicate Line |
| `Ctrl+L` / `Ctrl+Shift+K` | Delete Line |
| `Alt+Up` | Move Line Up |
| `Alt+Down` | Move Line Down |
| `Ctrl+Shift+Up` | Copy Line Up |
| `Ctrl+Shift+Down` | Copy Line Down |
| `Insert` | Toggle Insert/Overwrite |

### Navigation

| Shortcut | Action |
|----------|--------|
| `Ctrl+G` | Go to Line |
| `Ctrl+F` | Find |
| `Ctrl+H` | Find & Replace |
| `Ctrl+Shift+F` | Find in Tabs |
| `Ctrl+Tab` | Next Tab |
| `Ctrl+Shift+Tab` | Previous Tab |
| `Home` | Start of line (smart: first non-whitespace, then column 0) |
| `End` | End of line |
| `Ctrl+Home` | Start of document |
| `Ctrl+End` | End of document |
| `Ctrl+Left/Right` | Word jump |

### View

| Shortcut | Action |
|----------|--------|
| `Ctrl++` | Zoom In |
| `Ctrl+-` | Zoom Out |
| `Ctrl+0` | Reset Zoom |
| `F11` | Toggle Full Screen |
| `Ctrl+Shift+[` | Toggle Fold |
| `Ctrl+Shift+-` | Fold All |
| `Ctrl+Shift++` | Unfold All |
| `Escape` | Close Find Panel |

### Macros

| Shortcut | Action |
|----------|--------|
| `F5` | Play Macro |

### Mouse

| Action | Effect |
|--------|--------|
| Click | Place caret |
| Double-click | Select word |
| Triple-click | Select line |
| `Shift+Click` | Extend selection |
| `Alt+Click+Drag` | Column (box) selection |
| `Ctrl+Mouse Wheel` | Zoom in/out |
| Mouse Wheel | Scroll (configurable speed) |
| Drag file onto window | Open file |
