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

    public IReadOnlyList<CommandItem> AllCommands => _allCommands;

    public void EnsureBuilt()
    {
        if (_built)
            return;

        _built = true;
        _allCommands.Add(new CommandItem("New Tab", "File", vm => vm.NewTabCommand.Execute(null), "Ctrl+T"));
        _allCommands.Add(new CommandItem("Close Tab", "File", vm => vm.CloseSelectedTabCommand.Execute(null), "Ctrl+W"));
        _allCommands.Add(new CommandItem("Toggle Theme", "Appearance", vm => vm.ToggleThemeCommand.Execute(null), "Ctrl+Shift+T"));
        _allCommands.Add(new CommandItem("Toggle Dual Pane", "View", vm => vm.ToggleDualPaneCommand.Execute(null), "Ctrl+D"));
        _allCommands.Add(new CommandItem("Filter", "View", vm => vm.FocusFilterCommand.Execute(null), "Ctrl+F"));
        _allCommands.Add(new CommandItem("Search", "View", vm => vm.FocusSearchCommand.Execute(null), "Ctrl+Shift+F"));
        _allCommands.Add(new CommandItem("Settings", "View", vm => vm.OpenSettingsCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Go Back", "Navigation", vm => vm.ActivePane?.GoBackCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Go Forward", "Navigation", vm => vm.ActivePane?.GoForwardCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Go Up", "Navigation", vm => vm.ActivePane?.GoUpCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Refresh", "Navigation", vm => vm.ActivePane?.RefreshCommand.Execute(null), "F5"));
        _allCommands.Add(new CommandItem("New Folder", "File", vm => vm.NewFolderCommand.Execute(null), "Ctrl+Shift+N"));
        _allCommands.Add(new CommandItem("Details View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Details)));
        _allCommands.Add(new CommandItem("List View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.List)));
        _allCommands.Add(new CommandItem("Grid View", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Grid)));
        _allCommands.Add(new CommandItem("Miller Columns", "View", vm => vm.SetViewModeCommand.Execute(LayoutMode.Miller)));
        _allCommands.Add(new CommandItem("Show Hidden Items", "View", vm => vm.ToggleShowHiddenFilesCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Show File Extensions", "View", vm => vm.ToggleShowFileExtensionsCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Select All", "Selection", vm => vm.SelectAllCommand.Execute(null), "Ctrl+A"));
        _allCommands.Add(new CommandItem("Clear Selection", "Selection", vm => vm.ClearSelectionCommand.Execute(null), "Escape"));
        _allCommands.Add(new CommandItem("Invert Selection", "Selection", vm => vm.InvertSelectionCommand.Execute(null), "Ctrl+Shift+A"));
        _allCommands.Add(new CommandItem("Copy Path", "File", vm => vm.CopyPathCommand.Execute(null), "Ctrl+Shift+C"));
        _allCommands.Add(new CommandItem("Pin Current Folder", "View", vm => vm.PinCurrentFolderCommand.Execute(null)));
        _allCommands.Add(new CommandItem("Cut", "File", vm => vm.CutCommand.Execute(null), "Ctrl+X"));
        _allCommands.Add(new CommandItem("Copy", "File", vm => vm.CopyCommand.Execute(null), "Ctrl+C"));
        _allCommands.Add(new CommandItem("Paste", "File", vm => vm.PasteCommand.Execute(null), "Ctrl+V"));
        _allCommands.Add(new CommandItem("Delete", "File", vm => vm.DeleteCommand.Execute(null), "Delete"));
        _allCommands.Add(new CommandItem("Delete Permanently", "File", vm => vm.DeletePermanentlyCommand.Execute(null), "Shift+Delete"));
        _allCommands.Add(new CommandItem("Next Tab", "View", vm => vm.SelectNextTabCommand.Execute(null), "Ctrl+Tab"));
        _allCommands.Add(new CommandItem("Previous Tab", "View", vm => vm.SelectPreviousTabCommand.Execute(null), "Ctrl+Shift+Tab"));
        _allCommands.Add(new CommandItem("Rename", "File", vm => vm.RenameCommand.Execute(null), "F2"));
        _allCommands.Add(new CommandItem(
            "Open in Terminal",
            "File",
            vm => vm.OpenInTerminalCommand.Execute(null),
            MainWindowViewModel.AppDefaultTerminalGesture));
        _allCommands.Add(new CommandItem(
            "Git: Switch Branch",
            "Git",
            vm => _ = vm.ActivePane?.OpenBranchFlyoutCommand.ExecuteAsync(null)));
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
}
