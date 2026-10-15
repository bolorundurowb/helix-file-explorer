using System.Collections.ObjectModel;
using HelixExplorer.Core.Search;

namespace HelixExplorer.ViewModels;

/// <summary>
/// Builds the command palette catalog from <see cref="CommandRegistry"/> and applies fuzzy filtering (including recent paths).
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
        foreach (var definition in CommandRegistry.Palette)
        {
            _allCommands.Add(new CommandItem(
                definition.Label,
                definition.Category,
                definition.Execute,
                definition.Gesture,
                definition.CanExecute));
        }
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
