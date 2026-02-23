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

### 📦 Bascanka v.1.0.5 - 2026-02-23

#### Release notes

- horizontal scrolling and text measurement converted from cell-based to pixel-based (layout, scrollbar range, caret, and wrapping now use real pixels instead of fixed cells, enabling correct mixed-width rendering CJK, emoji, tabs, Latin).
- fixed horizontal scrollbar range not covering full line width - EstimateMaxLinePixelWidth now checks all lines by length (O(1) via cached offsets) and measures top 10 longest, not just visible lines.
- fixed cursor stuck on WrapMoveDown - now uses current column instead of sticky desired column (matches WrapMoveUp).
- fixed sub-character pixel offset at max horizontal scroll - added one MaxCharPixelWidth padding to scrollbar range.
- fixed caret not visible at end of long lines - ColumnToPixelOffset maps columns via ExpandedColumn for tabbed lines.
- word wrap is now automatically disabled for files over a configurable size limit (default 50 MB), with a new setting and localized labels.
- fixed an HTML lexer edge case that could spin in a loop on certain attributes, preventing UI hangs while scrolling.
- html inline CSS/JavaScript highlighting and folding is now supported within <style> and <script> blocks.
- json highlighting now distinguishes keys from string values with improved, more vivid colors for clearer readability.
- improved typing latency on huge single-line documents.
- improved scrolling performance on ultra-long lines.
- session persistence is now driven entirely by recovery\manifest.json, eliminating the duplicate session.json.
- find/replace search history now saves to search-history.json and loads at startup.
- the Chinese encoding option has been updated to GB18030 and renamed to "Chinese (GB18030)" across menus and localized strings.
- fixed CJK opening punctuation (《「『【 etc.) rendering issue - rendered now flush against the following character inside double-width cells, matching other CJK editors.
- the encoding detector now heuristically prefers GB18030 when UTF-8 decoding shows replacement characters, improving auto‑detection for CJK files.

---

- **Framework-dependent (small download - requires .NET 10 runtime)**  
  Single portable EXE (~2 MB). Use this if .NET 10 is already installed on your system.  
  👉 https://beegoesmoo.co.uk/bascanka/download/Bascanka.v.1.0.5.bin.zip  
  **SHA256:** `8EF1697027477196248E9CD7C4DF25307A6E2C44EAEA1C6AC392262C1816F4CC`

- **Self-contained (no runtime required)**  
  Single portable EXE with .NET 10 included (~120 MB). Works on any supported Windows machine without installing .NET.  
  👉 https://beegoesmoo.co.uk/bascanka/download/Bascanka.v.1.0.5.bin.sc.zip  
  **SHA256:** `00EB8943696753C9387471C18024F66D2CF9BD136DF657D19F0A3933EAD17043`

All builds are portable - no installation required.

---

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

##### Consider adding `-p:PublishReadyToRun=true`

`PublishReadyToRun=true` precompiles much of your app’s IL into native code **during publish**.
That means the app does **less JIT compilation at startup** and when code runs for the first time, so you typically get:

- **Faster startup time** (often noticeable for WinForms apps)
- **Less CPU spike on first launch / first UI actions**
- **No trimming / reflection compatibility issues** (unlike Native AOT)

Trade-offs:
- Publish output is **larger** (often +10–30%)
- Publish can take a bit longer

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

