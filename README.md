# Helix Explorer

[![Build and Test](https://github.com/bolorundurowb/helix-explorer/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/bolorundurowb/helix-explorer/actions/workflows/build-and-test.yml)

A modern Windows file manager built with Avalonia and .NET 10. Helix Explorer provides a Files-inspired experience with dual-pane browsing, tabs, archives, Git status, and deep Windows shell integration.

> **AI Notice:** This project was built with significant assistance from AI coding assistants.


## Why Helix Explorer?

I built Helix Explorer after experiencing persistent performance issues with the [Files](https://files.community/) app even on powerful hardware. Helix Explorer is my attempt at a faster, more responsive alternative (specifically by replacing WinUI 3 with Avalonia UI). Feedback and contributions are welcome.

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build and run

```powershell
dotnet build
dotnet run --project src/HelixExplorer
```

Open a specific folder in a new window:

```powershell
dotnet run --project src/HelixExplorer -- --path "C:\Users"
```

## Solution layout

| Project | Description |
|---------|-------------|
| `src/HelixExplorer` | Avalonia UI application |
| `src/HelixExplorer.Core` | Platform-agnostic domain logic |
| `src/HelixExplorer.Windows` | Windows file system and shell providers |
| `tests/HelixExplorer.Core.Tests` | Core unit tests |
| `tests/HelixExplorer.ViewModels.Tests` | ViewModel coordinator tests |

## Features

- Tabbed browsing with session restore
- Dual-pane mode (horizontal/vertical)
- Details, List, Grid, and Miller column views
- Copy/move conflict dialogs (Replace / Keep both / Skip)
- In-app Recycle Bin browsing with restore
- Archive browsing (ZIP, 7z, etc.)
- Git status coloring and branch switching
- Command palette and keyboard shortcuts
- Multiple Helix windows

## Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+T | New tab |
| Ctrl+W | Close tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+D | Toggle dual pane |
| Ctrl+F | Search current folder |
| Ctrl+X/C/V | Cut / Copy / Paste |
| Delete | Move to Recycle Bin |
| Shift+Delete | Permanently delete |
| F2 | Rename |
| Ctrl+Shift+N | New folder |
| Ctrl+Shift+P | Command palette |

## License

GPL-3.0 — see [LICENSE](LICENSE).

---

*Portions of this project were developed with AI assistance.*
