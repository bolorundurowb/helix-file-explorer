using CommunityToolkit.Mvvm.Messaging;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels;

public sealed record MainWindowViewModelDependencies(
    IThemeService ThemeService,
    IAccentBrushService AccentBrushes,
    IQuickAccessProvider QuickAccess,
    IClipboardService Clipboard,
    IArchiveProvider Archive,
    IFolderColorService FolderColors,
    IVolumeChangeWatcher VolumeWatcher,
    IPaneViewModelFactory PaneFactory,
    AppSettingsCoordinator SettingsCoordinator,
    SidebarViewModel Sidebar,
    CommandPaletteService CommandPalette,
    TabSessionCoordinator TabSession,
    FileOperationReporter OperationReporter,
    FileOperationUndoService Undo,
    IUserDialogService Dialogs,
    HomePageViewModel HomePage,
    WindowLayoutCoordinator WindowLayout,
    NetworkLocationCoordinator NetworkLocations,
    IMessenger Messenger);
