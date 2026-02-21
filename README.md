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

### 📦 Bascanka v.1.0.4 (2026-02-21)

#### Release notes

- fixed a bug where typing during a box (column) selection would produce corrupted text after the first few characters.
- fixed word wrap cut-off issue where characters on the right edge were clipped due to the text padding not being accounted for in wrap column calculations.
- fixed word wrap cursor navigation: arrow keys now move by visual wrap rows instead of jumping entire document lines when word wrap is enabled.
- fixed a bug where custom highlighting profiles assigned via Language > Custom Highlighting were not restored when reopening the application.
- word wrap is now saved per tab, so each document remembers its own setting across restarts and crashes, with new documents defaulting to the last-used state.
- open recent files menu now displays recent files in a cleaner two-column format.
- added move up and move down buttons for reordering custom highlighting profiles.
- fixed CJK (Chinese, Japanese, Korean) characters overlapping in the embedded terminal by correctly treating fullwidth characters as double-width cells.
- languages menu is now sorted ascending.
- fixed the Go To Line dialog so buttons are no longer clipped outside the window and the dialog now respects the active editor theme.

---

- **Framework-dependent (small download - requires .NET 10 runtime)**  
  Single portable EXE (~2 MB). Use this if .NET 10 is already installed on your system.  
  👉 https://beegoesmoo.co.uk/bascanka/download/Bascanka.v.1.0.4.bin.zip  
  **SHA256:** `8A9BD9BB83E8DC9E42F6E687C35899E8E88BF92374B28EC86931E8EDA448BAC5`

- **Self-contained (no runtime required)**  
  Single portable EXE with .NET 10 included (~120 MB). Works on any supported Windows machine without installing .NET.  
  👉 https://beegoesmoo.co.uk/bascanka/download/Bascanka.v.1.0.4.bin.sc.zip  
  **SHA256:** `05B294137C2C608953662A48068700458E31EAB6941CDDD18C969C45EF4531B7`

All builds are portable - no installation required.

### 📦 Bascanka v.1.0.3

#### Release notes

- implemented embedded terminal (cmd.exe) to the bottom panel using the Windows ConPTY API.
- added close/crash unsaved changes recovery with periodic automatic back-ups to "%AppData%\Bascanka\recovery" - small/untitled files are backed up in full size as UTF-8, while large memory-mapped files use efficient binary delta/changes-only save format.
- fixed emoji and special character rendering, cursor positioning, selection, and caret navigation - including surrogate pairs, ZWJ sequences and variation selectors.
- lines containing wide or special characters are now rendered character-by-character to prevent GDI font fallback from causing cursor drift.
- improved Unicode character support.
- implemented a pending-delta mechanism for the line offset cache — consecutive same-line edits accumulate a virtual shift in O(1) instead of triggering an O(N) full-document rescan.
- added double-click on empty tab bar area to create a new untitled document.
- added tabbed bottom panel - Find Results and Terminal now share a tabbed bottom panel.
- fixed search history not being saved correctly.
- added Modern Windows Context Menu registration from Settings ("Edit with Bascanka"), with automatic sparse package and COM shell extension setup.
- only one Bascanka instance now runs at a time - opening files while it's running forwards them to the existing window via named pipes.

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
  Bascanka.App/           # Application, menus, localization
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

