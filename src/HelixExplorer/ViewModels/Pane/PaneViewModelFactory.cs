using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.FileSystem.Undo;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Pane;

public interface IPaneViewModelFactory
{
    PaneViewModel Create();
}

public sealed class PaneViewModelFactory(
    IFileSystemProvider fileSystem,
    IArchiveProvider archive,
    IFolderColorService folderColors,
    IFolderViewPreferencesService folderViewPrefs,
    IFileOperationService fileOps,
    IClipboardService clipboard,
    IUiHost uiHost,
    IGitProvider git,
    Func<IFileChangeWatcher> watcherFactory,
    AppSettingsCoordinator settings,
    IQuickAccessProvider quickAccess,
    IUserDialogService dialogs,
    IWindowHostService windowHost,
    IRecycleBinService recycleBin,
    IFileOperationHistory history,
    FileVisualService visuals,
    IOsFileClipboard osClipboard,
    IFileOperationReporter operationReporter,
    IShellContextMenuService shellContextMenu,
    ITerminalLauncher terminalLauncher,
    ILoggerFactory loggerFactory,
    ILogger<PaneViewModel> logger) : IPaneViewModelFactory
{
    public PaneViewModel Create()
        => new(
            fileSystem,
            archive,
            folderColors,
            folderViewPrefs,
            fileOps,
            clipboard,
            uiHost,
            git,
            watcherFactory(),
            settings,
            quickAccess,
            dialogs,
            windowHost,
            recycleBin,
            history,
            new PaneFileOperationCoordinator(
                fileOps,
                clipboard,
                osClipboard,
                dialogs,
                operationReporter,
                history,
                loggerFactory.CreateLogger<PaneFileOperationCoordinator>()),
            new PaneRefreshCoordinator(
                fileSystem,
                archive,
                git,
                visuals,
                loggerFactory.CreateLogger<PaneRefreshCoordinator>()),
            new PaneSearchCoordinator(loggerFactory.CreateLogger<PaneSearchCoordinator>()),
            new PaneShellActionCoordinator(
                shellContextMenu,
                terminalLauncher,
                loggerFactory.CreateLogger<PaneShellActionCoordinator>()),
            logger);
}
