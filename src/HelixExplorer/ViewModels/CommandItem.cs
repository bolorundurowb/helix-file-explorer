using CommunityToolkit.Mvvm.ComponentModel;
using HelixExplorer.Core.Search;

namespace HelixExplorer.ViewModels;

public sealed class CommandItem : ObservableObject
{
    private const double DisabledRowOpacity = 0.4;

    private readonly Func<MainWindowViewModel, bool>? _canExecute;

    public CommandItem(
        string title,
        string category,
        Action<MainWindowViewModel> execute,
        string shortcut = "",
        Func<MainWindowViewModel, bool>? canExecute = null)
    {
        Title = title;
        Category = category;
        Shortcut = shortcut;
        Execute = execute;
        _canExecute = canExecute;
    }

    public string Title { get; }
    public string Category { get; }
    public string Shortcut { get; }
    public Action<MainWindowViewModel>? Execute { get; }

    public string SearchText => $"{Title} {Category} {Shortcut}";

    public int FuzzyScore(string query) => FuzzyMatcher.Score(SearchText, query);

    /// <summary>False keeps the row in the list; the palette must not run it.</summary>
    public bool IsAvailable { get; private set; } = true;

    public double RowOpacity { get; private set; } = 1;

    public void RefreshAvailability(MainWindowViewModel vm)
    {
        var available = _canExecute?.Invoke(vm) ?? true;
        if (available == IsAvailable)
            return;

        IsAvailable = available;
        // Same opacity as a disabled tool button, so an unavailable command still reads as disabled.
        RowOpacity = available ? 1 : DisabledRowOpacity;
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(RowOpacity));
    }
}
