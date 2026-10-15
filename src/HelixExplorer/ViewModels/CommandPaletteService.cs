using System.Collections.ObjectModel;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Search;

namespace HelixExplorer.ViewModels;

/// <summary>
/// Builds the command palette catalog and applies fuzzy filtering (including recent paths).
/// </summary>
public sealed class CommandPaletteService
{
    private const int MaxPaletteResults = 24;

    private readonly List<CommandItem> _allCommands = new();
    private bool _built;

    public void EnsureBuilt()
    {
        if (_built)
            return;

        _built = true;
        Add("New Tab", "File", vm => vm.NewTabCommand.Execute(null), "Ctrl+T");
        Add("Close Tab", "File", vm => vm.CloseSelectedTabCommand.Execute(null), "Ctrl+W");
        Add("Toggle Theme", "Appearance", vm => vm.ToggleThemeCommand.Execute(null));
        Add("New Window", "File", vm => vm.NewWindowCommand.Execute(null), "Ctrl+N");
        Add("Reopen Closed Tab", "File", vm => vm.ReopenClosedTabCommand.Execute(null), "Ctrl+Shift+T", CanReopenClosedTab);
        Add("Toggle Sidebar", "View", vm => vm.ToggleSidebarCommand.Execute(null));
        Add("Toggle Dual Pane", "View", vm => vm.ToggleDualPaneCommand.Execute(null), "Ctrl+D");
        Add("Filter", "View", vm => vm.FocusFilterCommand.Execute(null), "Ctrl+F");
        Add("Search", "View", vm => vm.FocusSearchCommand.Execute(null), "Ctrl+Shift+F");
        Add("Settings", "View", vm => vm.OpenSettingsCommand.Execute(null));
        Add("Toggle Status Centre", "View", vm => vm.ToggleStatusCentreCommand.Execute(null), "Ctrl+Shift+J");
        Add("Go Back", "Navigation", vm => vm.ActivePane?.GoBackCommand.Execute(null), canExecute: CanGoBack);
        Add("Go Forward", "Navigation", vm => vm.ActivePane?.GoForwardCommand.Execute(null), canExecute: CanGoForward);
        Add("Go Up", "Navigation", vm => vm.ActivePane?.GoUpCommand.Execute(null), canExecute: CanGoUp);
        Add("Refresh", "Navigation", vm => vm.ActivePane?.RefreshCommand.Execute(null), "F5");
        Add("New Folder", "File", vm => vm.NewFolderCommand.Execute(null), "Ctrl+Shift+N", CanCreateFolder);
        Add("Details View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Details));
        Add("List View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.List));
        Add("Grid View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Grid));
        Add("Columns View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Miller));
        Add("Show Hidden Items", "View", vm => vm.ToggleShowHiddenFilesCommand.Execute(null));
        Add("Show File Extensions", "View", vm => vm.ToggleShowFileExtensionsCommand.Execute(null));
        Add("Select All", "Selection", vm => vm.SelectAllCommand.Execute(null), "Ctrl+A", CanSelectAll);
        Add("Clear Selection", "Selection", vm => vm.ClearSelectionCommand.Execute(null), "Escape", CanClearSelection);
        Add("Invert Selection", "Selection", vm => vm.InvertSelectionCommand.Execute(null), "Ctrl+Shift+A", CanInvertSelection);
        Add("Copy Path", "File", vm => vm.CopyPathCommand.Execute(null), "Ctrl+Shift+C", CanCopyPath);
        Add("Pin Current Folder", "View", vm => vm.PinCurrentFolderCommand.Execute(null), canExecute: CanPinCurrentFolder);
        Add("Undo", "File", vm => _ = vm.UndoCommand.ExecuteAsync(null), "Ctrl+Z", CanUndo);
        Add("Redo", "File", vm => _ = vm.RedoCommand.ExecuteAsync(null), "Ctrl+Y", CanRedo);
        Add("Cut", "File", vm => vm.CutCommand.Execute(null), "Ctrl+X", CanCut);
        Add("Copy", "File", vm => vm.CopyCommand.Execute(null), "Ctrl+C", CanCopy);
        Add("Copy to Other Pane", "File", vm => _ = vm.CopyToOtherPaneCommand.ExecuteAsync(null), "Alt+F5", CanCopyToOtherPane);
        Add("Move to Other Pane", "File", vm => _ = vm.MoveToOtherPaneCommand.ExecuteAsync(null), "Alt+F6", CanMoveToOtherPane);
        Add("Paste", "File", vm => vm.PasteCommand.Execute(null), "Ctrl+V", CanPaste);
        Add("Delete", "File", vm => vm.DeleteCommand.Execute(null), "Delete", CanDelete);
        Add("Delete Permanently", "File", vm => vm.DeletePermanentlyCommand.Execute(null), "Shift+Delete", CanDeletePermanently);
        Add("Next Tab", "View", vm => vm.SelectNextTabCommand.Execute(null), "Ctrl+Tab");
        Add("Previous Tab", "View", vm => vm.SelectPreviousTabCommand.Execute(null), "Ctrl+Shift+Tab");
        Add("Rename", "File", vm => vm.RenameCommand.Execute(null), "F2", CanRename);
        Add(
            "Open in Terminal",
            "File",
            vm => vm.OpenInTerminalCommand.Execute(null),
            MainWindowViewModel.AppDefaultTerminalGesture,
            CanOpenInTerminal);
        Add(
            "Git: Switch Branch",
            "Git",
            vm => _ = vm.ActivePane?.OpenBranchFlyoutCommand.ExecuteAsync(null));
    }

    public void FilterInto(
        ObservableCollection<CommandItem> target,
        string query,
        IReadOnlyList<string> recentPaths)
    {
        EnsureBuilt();
        target.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var command in _allCommands)
                target.Add(command);
            return;
        }

        var scored = new List<(int score, CommandItem item)>();
        foreach (var command in _allCommands)
        {
            var score = command.FuzzyScore(query);
            if (score >= 0)
                scored.Add((score, command));
        }

        foreach (var path in recentPaths)
        {
            var score = FuzzyMatcher.Score(path, query);
            if (score >= 0)
            {
                var captured = path;
                scored.Add((score, new CommandItem(path, "Recent", vm => vm.NavigateActive(captured))));
            }
        }

        foreach (var entry in scored.OrderByDescending(t => t.score).Take(MaxPaletteResults))
            target.Add(entry.item);
    }

    private void Add(
        string title,
        string category,
        Action<MainWindowViewModel> execute,
        string shortcut = "",
        Func<MainWindowViewModel, bool>? canExecute = null)
        => _allCommands.Add(new CommandItem(title, category, execute, shortcut, canExecute));

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
