# Helix Explorer

[![Build and Test](https://github.com/bolorundurowb/helix-file-explorer/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/bolorundurowb/helix-file-explorer/actions/workflows/build-and-test.yml)

A fast Windows file manager built with Avalonia and .NET 10. Dual panes, tabs, archives, Git status, and native shell integration without WinUI 3.

Built after persistent performance issues with [Files](https://files.community/), even on capable hardware. Feedback and contributions welcome.

## Features

- Tabbed browsing with session restore
- Dual-pane layout (horizontal or vertical)
- Details, List, Grid, and Miller column views
- Copy/move conflict dialogs (Replace / Keep both / Skip)
- Recycle Bin browse and restore
- Archive browsing (ZIP, 7z, and related formats)
- Git status coloring and branch switching
- Command palette and multi-window support

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+T | New tab |
| Ctrl+W | Close tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+D | Toggle dual pane |
| Ctrl+F | Search current folder |
| Ctrl+X / C / V | Cut / Copy / Paste |
| Ctrl+A | Select all |
| Ctrl+Shift+C | Copy path |
| Ctrl+Shift+N | New folder |
| Ctrl+Shift+P | Command palette |
| Ctrl+Shift+T | Cycle theme |
| Delete | Move to Recycle Bin |
| Shift+Delete | Permanently delete |
| F2 | Rename |

## Development

Requirements:

- Windows 10 or 11
- [.NET 10 SDK](https://dot.net)

Build and run:

```powershell
dotnet build
dotnet run --project src/HelixExplorer
```

Open a folder in a new window:

```powershell
dotnet run --project src/HelixExplorer -- --path "C:\Users"
```

Run tests:

```powershell
dotnet test
```

## License

[GPL-3.0](LICENSE)

---

*Portions of this project were developed with AI assistance.*
