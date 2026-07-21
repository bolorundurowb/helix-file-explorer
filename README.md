<p><img src="assets/helix-explorer-icon.png" alt="Helix Explorer icon" width="128" /></p>

# Helix Explorer

[![Build and Test](https://github.com/bolorundurowb/helix-file-explorer/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/bolorundurowb/helix-file-explorer/actions/workflows/build-and-test.yml)

A fast, modern file manager for Windows.

## Overview

Helix Explorer helps you browse, organize, and move files without the sluggishness of heavier alternatives. It is built for people who work with folders every day—developers, power users, and anyone who wants a clearer, more controllable Windows file manager.

Use it when you need dual-pane workflows, tabs that restore between sessions, archive browsing, Git status at a glance, and familiar Windows shell features (Recycle Bin, network locations, context menus) in one place.

## Features

- **Tabbed browsing** with session restore when you reopen the app
- **Dual panes** with horizontal or vertical split for copy and compare workflows
- **Multiple views** — Details, List, Grid (with thumbnails), and Miller columns
- **Quick filter and search** — filter the current folder (globs supported) or search files and content recursively
- **Reliable file operations** — cut, copy, paste, rename, and conflict dialogs (Replace / Keep both / Skip)
- **Recycle Bin** browse and restore
- **Archive support** — browse ZIP, 7z, RAR, TAR, and related formats; compress to ZIP; extract here
- **Git status** coloring and branch switching from the status bar or command palette
- **Home page** with quick access, drives, and network locations
- **Sidebar pins**, folder colors, and drag-and-drop
- **Command palette**, multi-window support, and Open in Terminal
- **Themes** — System, Light, or Dark, plus accent colors and font choices

## Installation

### Requirements

- Windows 10 or Windows 11 (64-bit)

### Download and install

1. Open the [latest release](https://github.com/bolorundurowb/helix-file-explorer/releases/latest).
2. Download `Helix.Explorer-*-windows-x64.exe`.
3. Run the installer and follow the prompts.
4. Launch **Helix Explorer** from the Start menu or desktop shortcut.

No separate runtime install is required. Settings and open tabs are stored under `%AppData%\HelixExplorer` on first run.

## Getting Started

1. Launch Helix Explorer. You land on the **Home** page with quick access, drives, and network locations.
2. Open a folder from the sidebar, Home, or by typing a path in the address bar.
3. Press `Ctrl+T` to open another tab, or `Ctrl+D` to turn on dual panes.
4. Select files and use `Ctrl+C` / `Ctrl+V` to copy them, or drag them between panes or into the sidebar.
5. Press `Ctrl+Shift+P` to open the command palette when you want to jump to a command or recent path quickly.

Tip: Press `Ctrl+F` to filter the current folder, or `Ctrl+Shift+F` to search recursively.

## Core Functionality

### Browse and navigate

- Use the sidebar for pinned folders, known locations, volumes, Recycle Bin, and network.
- Switch between Details, List, Grid, and Miller column layouts from the toolbar.
- Open folders in a new window from the context menu when you need a separate workspace.
- Breadcrumbs work for local paths, UNC/network paths, and archives.

### Manage files and folders

- Cut, copy, paste, rename (`F2`), new folder (`Ctrl+Shift+N`), and copy path (`Ctrl+Shift+C`).
- Delete sends items to the Recycle Bin; `Shift+Delete` permanently deletes.
- When names conflict during copy or move, choose Replace, Keep both, or Skip (optionally apply to all).
- Watch long operations in the status centre — pause, resume, or cancel as needed.
- Use native Properties and Windows “Show more options” from the context menu.

### Work with archives and Git

- Open supported archives as folders; extract contents or compress selections to ZIP.
- In Git repositories, see status coloring on files and switch branches from the status bar or command palette.

### Keyboard shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+T` | New tab |
| `Ctrl+W` | Close tab |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Next / previous tab |
| `Ctrl+D` | Toggle dual pane |
| `Ctrl+F` | Filter current folder |
| `Ctrl+Shift+F` | Search files and content |
| `Ctrl+X` / `C` / `V` | Cut / Copy / Paste |
| `Ctrl+A` | Select all |
| `Ctrl+Shift+C` | Copy path |
| `Ctrl+Shift+N` | New folder |
| `Ctrl+Shift+P` | Command palette |
| `Ctrl+Shift+T` | Cycle theme |
| Ctrl+` | Open in Terminal (customizable) |
| `Delete` | Move to Recycle Bin |
| `Shift+Delete` | Permanently delete |
| `F2` | Rename |
| `F5` | Refresh |
| `Ctrl+Scroll` | Thumbnail size (Grid view) |

## Configuration

Open **Settings** from the app to adjust:

| Area | What you can change |
|------|---------------------|
| Appearance | Theme, accent color, UI font |
| Layout | Default view mode, dual pane, split orientation, thumbnail size |
| Files & folders | Hidden files, file extensions, sort order, size units (binary/decimal) |
| General | Open in Terminal shortcut, automatic update checks |

Pinned folders, folder colors, window size/position, and open tabs are remembered automatically.

Settings file: `%AppData%\HelixExplorer\settings.json`

## Documentation

- [Releases and download](https://github.com/bolorundurowb/helix-file-explorer/releases)
- [Report an issue](https://github.com/bolorundurowb/helix-file-explorer/issues)

## Contributing

Bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to get started.

## License

[GPL-3.0](LICENSE)

---

*Portions of this project were developed with AI assistance.*
