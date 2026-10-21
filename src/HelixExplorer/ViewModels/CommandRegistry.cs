using HelixExplorer.Core.Models;

namespace HelixExplorer.ViewModels;

/// <summary>
/// The one list of commands the palette and the generated Sort, Group by, and Layout menus are built from.
/// Pane menus are groups of radio options; a separator goes between groups.
/// </summary>
public static class CommandRegistry
{
    public static IReadOnlyList<CommandDefinition<MainWindowViewModel>> Palette { get; } =
    [
        Window("file.newTab", "New Tab", "File", vm => vm.NewTabCommand.Execute(null), "Ctrl+T"),
        Window("file.closeTab", "Close Tab", "File", vm => vm.CloseSelectedTabCommand.Execute(null), "Ctrl+W"),
        Window("appearance.toggleTheme", "Toggle Theme", "Appearance", vm => vm.ToggleThemeCommand.Execute(null)),
        Window("file.newWindow", "New Window", "File", vm => vm.NewWindowCommand.Execute(null), "Ctrl+N"),
        Window("file.reopenClosedTab", "Reopen Closed Tab", "File", vm => vm.ReopenClosedTabCommand.Execute(null), "Ctrl+Shift+T", CanReopenClosedTab),
        Window("view.toggleSidebar", "Toggle Sidebar", "View", vm => vm.ToggleSidebarCommand.Execute(null)),
        Window("view.toggleDualPane", "Toggle Dual Pane", "View", vm => vm.ToggleDualPaneCommand.Execute(null), "Ctrl+D"),
        Window("view.filter", "Filter", "View", vm => vm.FocusFilterCommand.Execute(null), "Ctrl+F"),
        Window("view.search", "Search", "View", vm => vm.FocusSearchCommand.Execute(null), "Ctrl+Shift+F"),
        Window("view.settings", "Settings", "View", vm => vm.OpenSettingsCommand.Execute(null)),
        Window("view.toggleStatusCentre", "Toggle Status Centre", "View", vm => vm.ToggleStatusCentreCommand.Execute(null), "Ctrl+Shift+J"),
        Window("navigation.back", "Go Back", "Navigation", vm => vm.ActivePane?.GoBackCommand.Execute(null), canExecute: CanGoBack),
        Window("navigation.forward", "Go Forward", "Navigation", vm => vm.ActivePane?.GoForwardCommand.Execute(null), canExecute: CanGoForward),
        Window("navigation.up", "Go Up", "Navigation", vm => vm.ActivePane?.GoUpCommand.Execute(null), canExecute: CanGoUp),
        Window("navigation.refresh", "Refresh", "Navigation", vm => vm.ActivePane?.RefreshCommand.Execute(null), "F5"),
        Window("file.newFolder", "New Folder", "File", vm => vm.NewFolderCommand.Execute(null), "Ctrl+Shift+N", CanCreateFolder),
        Window("view.layout.details", "Details View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Details)),
        Window("view.layout.list", "List View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.List)),
        Window("view.layout.grid", "Grid View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Grid)),
        Window("view.layout.miller", "Columns View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Miller)),
        Window("view.showHidden", "Show Hidden Items", "View", vm => vm.ToggleShowHiddenFilesCommand.Execute(null)),
        Window("view.showExtensions", "Show File Extensions", "View", vm => vm.ToggleShowFileExtensionsCommand.Execute(null)),
        Window("selection.all", "Select All", "Selection", vm => vm.SelectAllCommand.Execute(null), "Ctrl+A", CanSelectAll),
        Window("selection.clear", "Clear Selection", "Selection", vm => vm.ClearSelectionCommand.Execute(null), "Escape", CanClearSelection),
        Window("selection.invert", "Invert Selection", "Selection", vm => vm.InvertSelectionCommand.Execute(null), "Ctrl+Shift+A", CanInvertSelection),
        Window("file.copyPath", "Copy Path", "File", vm => vm.CopyPathCommand.Execute(null), "Ctrl+Shift+C", CanCopyPath),
        Window("view.pinCurrentFolder", "Pin Current Folder", "View", vm => vm.PinCurrentFolderCommand.Execute(null), canExecute: CanPinCurrentFolder),
        Window("file.undo", "Undo", "File", vm => _ = vm.UndoCommand.ExecuteAsync(null), "Ctrl+Z", CanUndo),
        Window("file.redo", "Redo", "File", vm => _ = vm.RedoCommand.ExecuteAsync(null), "Ctrl+Y", CanRedo),
        Window("file.cut", "Cut", "File", vm => vm.CutCommand.Execute(null), "Ctrl+X", CanCut),
        Window("file.copy", "Copy", "File", vm => vm.CopyCommand.Execute(null), "Ctrl+C", CanCopy),
        Window("file.copyToOtherPane", "Copy to Other Pane", "File", vm => _ = vm.CopyToOtherPaneCommand.ExecuteAsync(null), "Alt+F5", CanCopyToOtherPane),
        Window("file.moveToOtherPane", "Move to Other Pane", "File", vm => _ = vm.MoveToOtherPaneCommand.ExecuteAsync(null), "Alt+F6", CanMoveToOtherPane),
        Window("file.paste", "Paste", "File", vm => vm.PasteCommand.Execute(null), "Ctrl+V", CanPaste),
        Window("file.delete", "Delete", "File", vm => vm.DeleteCommand.Execute(null), "Delete", CanDelete),
        Window("file.deletePermanently", "Delete Permanently", "File", vm => vm.DeletePermanentlyCommand.Execute(null), "Shift+Delete", CanDeletePermanently),
        Window("view.nextTab", "Next Tab", "View", vm => vm.SelectNextTabCommand.Execute(null), "Ctrl+Tab"),
        Window("view.previousTab", "Previous Tab", "View", vm => vm.SelectPreviousTabCommand.Execute(null), "Ctrl+Shift+Tab"),
        Window("file.rename", "Rename", "File", vm => vm.RenameCommand.Execute(null), "F2", CanRename),
        Window(
            "file.openInTerminal",
            "Open in Terminal",
            "File",
            vm => vm.OpenInTerminalCommand.Execute(null),
            MainWindowViewModel.AppDefaultTerminalGesture,
            CanOpenInTerminal),
        Window("git.switchBranch", "Git: Switch Branch", "Git", vm => _ = vm.ActivePane?.OpenBranchFlyoutCommand.ExecuteAsync(null)),
    ];

    public static IReadOnlyList<IReadOnlyList<CommandDefinition<PaneViewModel>>> SortMenu { get; } =
    [
        [
            Option("sort.column.name", "Name", "Sort", p => p.SortColumn = SortColumn.Name, p => p.SortColumn == SortColumn.Name),
            Option("sort.column.modified", "Date", "Sort", p => p.SortColumn = SortColumn.Modified, p => p.SortColumn == SortColumn.Modified),
            Option("sort.column.type", "Type", "Sort", p => p.SortColumn = SortColumn.Type, p => p.SortColumn == SortColumn.Type),
            Option("sort.column.size", "Size", "Sort", p => p.SortColumn = SortColumn.Size, p => p.SortColumn == SortColumn.Size),
        ],
        [
            Option("sort.ascending", "Ascending", "Sort", p => p.SortDescending = false, p => !p.SortDescending),
            Option("sort.descending", "Descending", "Sort", p => p.SortDescending = true, p => p.SortDescending),
        ],
        [
            DirectorySortOption("sort.foldersFirst", "Sort folders first", DirectorySortMode.FoldersFirst),
            DirectorySortOption("sort.filesFirst", "Sort files first", DirectorySortMode.FilesFirst),
            DirectorySortOption("sort.mixed", "Sort files and folders together", DirectorySortMode.MixedWithFiles),
        ],
    ];

    public static IReadOnlyList<IReadOnlyList<CommandDefinition<PaneViewModel>>> GroupByMenu { get; } =
    [
        [GroupOption("group.none", "None", GroupByMode.None)],
        [
            GroupOption("group.name", "Name", GroupByMode.Name),
            GroupOption("group.modified", "Date modified", GroupByMode.Modified),
            GroupOption("group.type", "Type", GroupByMode.Type),
            GroupOption("group.size", "Size", GroupByMode.Size),
        ],
    ];

    public static IReadOnlyList<IReadOnlyList<CommandDefinition<PaneViewModel>>> LayoutMenu { get; } =
    [
        [
            LayoutOption("layout.details", "Details", LayoutMode.Details, "HelixIconTableGeometry"),
            LayoutOption("layout.list", "List", LayoutMode.List, "HelixIconListGeometry"),
            LayoutOption("layout.grid", "Grid", LayoutMode.Grid, "HelixIconGridGeometry"),
            LayoutOption("layout.miller", "Columns", LayoutMode.Miller, "HelixIconLayoutColumnThreeGeometry"),
        ],
    ];

    private static CommandDefinition<MainWindowViewModel> Window(
        string id,
        string label,
        string category,
        Action<MainWindowViewModel> execute,
        string gesture = "",
        Func<MainWindowViewModel, bool>? canExecute = null)
        => new(id, label, category, null, gesture, execute, canExecute);

    private static CommandDefinition<PaneViewModel> Option(
        string id,
        string label,
        string category,
        Action<PaneViewModel> execute,
        Func<PaneViewModel, bool> isChecked,
        string? icon = null)
        => new(id, label, category, icon, string.Empty, execute, IsChecked: isChecked);

    private static CommandDefinition<PaneViewModel> DirectorySortOption(string id, string label, DirectorySortMode mode)
        => Option(id, label, "Sort", p => p.DirectorySort = mode, p => p.DirectorySort == mode);

    private static CommandDefinition<PaneViewModel> GroupOption(string id, string label, GroupByMode mode)
        => Option(id, label, "Group by", p => p.GroupBy = mode, p => p.GroupBy == mode);

    private static CommandDefinition<PaneViewModel> LayoutOption(string id, string label, LayoutMode mode, string icon)
        => Option(id, label, "Layout", p => p.SetViewMode(mode), p => p.ViewMode == mode, icon);

    // Window file commands report CanExecute false while the palette query box is focused.
    // These predicates keep that text-focus gate out so a row is dimmed only when the operation itself cannot run.
    private static bool CanReopenClosedTab(MainWindowViewModel vm) => vm.HasClosedTab;

    private static bool CanGoBack(MainWindowViewModel vm)
        => vm.ActivePane?.GoBackCommand.CanExecute(null) == true;

    private static bool CanGoForward(MainWindowViewModel vm)
        => vm.ActivePane?.GoForwardCommand.CanExecute(null) == true;

    private static bool CanGoUp(MainWindowViewModel vm)
        => vm.ActivePane?.GoUpCommand.CanExecute(null) == true;

    private static bool CanCreateFolder(MainWindowViewModel vm)
        => vm.ActivePane is { IsRenaming: false, CanCreateFolderHere: true };

    private static bool CanSelectAll(MainWindowViewModel vm)
        => vm.ActivePane is { IsRenaming: false, CanSelectAllHere: true };

    private static bool CanClearSelection(MainWindowViewModel vm)
        => vm.ActivePane is { IsRenaming: false, SelectedCount: > 0 };

    private static bool CanInvertSelection(MainWindowViewModel vm) => CanSelectAll(vm);

    private static bool CanCopyPath(MainWindowViewModel vm)
        => vm.ActivePane is { IsRenaming: false } pane && pane.CanCopyPath();

    private static bool CanPinCurrentFolder(MainWindowViewModel vm)
        => vm.PinCurrentFolderCommand.CanExecute(null);

    private static bool CanUndo(MainWindowViewModel vm)
        => vm.ActivePane is not { IsRenaming: true }
           && !vm.OperationReporter.IsBusy
           && vm.UndoDescription is { Length: > 0 };

    private static bool CanRedo(MainWindowViewModel vm)
        => vm.ActivePane is not { IsRenaming: true }
           && !vm.OperationReporter.IsBusy
           && vm.RedoDescription is { Length: > 0 };

    private static bool CanCut(MainWindowViewModel vm)
        => vm.ActivePane is { IsRenaming: false, HasSelectionForOps: true };

    private static bool CanCopy(MainWindowViewModel vm) => CanCut(vm);

    private static bool CanCopyToOtherPane(MainWindowViewModel vm)
        => vm.ActivePane is not { IsRenaming: true }
           && vm.SelectedTab?.CanCopyToOtherPane == true;

    private static bool CanMoveToOtherPane(MainWindowViewModel vm)
        => vm.ActivePane is not { IsRenaming: true }
           && vm.SelectedTab?.CanMoveToOtherPane == true;

    private static bool CanPaste(MainWindowViewModel vm)
        => vm.ActivePane is { IsRenaming: false, CanPasteHere: true };

    private static bool CanDelete(MainWindowViewModel vm)
        => vm.ActivePane is { IsRenaming: false, HasSelectionForDelete: true };

    private static bool CanDeletePermanently(MainWindowViewModel vm)
        => vm.ActivePane is { IsRenaming: false, HasSelectionForDeletePerm: true };

    private static bool CanRename(MainWindowViewModel vm)
        => vm.ActivePane is { IsRenaming: false, HasSingleSelectionForOps: true };

    // The window command refuses while the palette is open so its key binding does not fire.
    // The pane command is the availability the row should show.
    private static bool CanOpenInTerminal(MainWindowViewModel vm)
        => vm.ActivePane?.OpenInTerminalCommand.CanExecute(null) == true;
}
