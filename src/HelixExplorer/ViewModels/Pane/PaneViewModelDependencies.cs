using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.FileSystem.Undo;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Pane;

public sealed record PaneViewModelDependencies(
    IFileSystemProvider FileSystem,
    IArchiveProvider Archive,
    IFolderColorService FolderColors,
    IFolderViewPreferencesService FolderViewPreferences,
    IClipboardService Clipboard,
    IUiHost UiHost,
    IGitProvider Git,
    IFileChangeWatcher Watcher,
    ISettingsStore SettingsStore,
    IQuickAccessProvider QuickAccess,
    IUserDialogService Dialogs,
    IWindowHostService WindowHost,
    IShellFolderEnumerator ShellEnumerator,
    IFileOperationHistory History,
    IPaneCoordinatorFactory CoordinatorFactory,
    ILogger<PaneViewModel> Logger,
    IExternalFileDragService ExternalFileDragService,
    IExternalFileDragPayloadBuilder DragPayloadBuilder);
