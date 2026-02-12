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

### 📦 Bascanka v1.0.0

- **Framework-dependent (small download - requires .NET 10 runtime)**  
  Single portable EXE (~2 MB). Use this if .NET 10 is already installed on your system.  
  👉 https://beegoesmoo.co.uk/bascanka/download/Bascanka.v.1.0.0.bin.zip  
  **SHA256:** `9C6CF2EE34A72CC3C67D17F0E177CF8DB15B23A45ACEB8296DD2634B909BDFA8`

- **Self-contained (no runtime required)**  
  Single portable EXE with .NET 10 included (~120 MB). Works on any supported Windows machine without installing .NET.  
  👉 https://beegoesmoo.co.uk/bascanka/download/Bascanka.v.1.0.0.bin.sc.zip  
  **SHA256:** `8DCCD9F8250402CA12CC1A55A867115EC3FC6821D8121F3B2920FEFB891A27F7`

All builds are portable - no installation required.

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_md.png" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_md.png"
         alt="Bascanka main screen"
         width="100%">
  </a>
</p>

## Screenshots

#### Text editor and Hex Editor

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_main.png" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_main.png"
         alt="Bascanka main screen"
         width="100%">
  </a>
</p>

#### Syntax highlighting

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_main.png" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_js.png"
         alt="Bascanka main screen"
         width="100%">
  </a>
</p>

#### Hex editor

<p align="center">
  <a href="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_hex.png" target="_blank">
    <img src="https://raw.githubusercontent.com/jhabjan/bascanka/refs/heads/main/docs/resources/screen_hex.png"
         alt="Bascanka main screen"
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
