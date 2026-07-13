# HelixExplorer.Windows

Windows-specific implementations of Core interfaces.

## Providers

| Type | Implementation |
|------|----------------|
| File system | `WinFileSystemProvider`: directory enumeration with shell namespace support |
| File operations | `WinFileOperationService`: copy/move/delete with conflict resolution |
| Shell folders | `WinShellFolderEnumerator`: Recycle Bin and other `shell:` paths via COM |
| Shell context menu | `WinShellContextMenuService`: native "Show more options" |
| File icons | `WinFileVisualProvider`: `SHGetFileInfo` icons and thumbnails |
| Quick access | `WinQuickAccessProvider`: known folders and pinned defaults |
| Volumes | `WinVolumeProvider`: drive enumeration |
| Network | `WinNetworkLocationProvider`: network share discovery |
| File watcher | `FileChangeWatcherService`: directory change notifications |
| Theme | `WinThemeWatcher`: OS light/dark theme sync |

Register all services via `WindowsServiceExtensions.AddHelixWindowsServices()`.
