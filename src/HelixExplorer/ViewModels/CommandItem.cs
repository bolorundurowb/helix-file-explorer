using HelixExplorer.Core.Search;

namespace HelixExplorer.ViewModels;

public sealed class CommandItem(
    string title,
    string category,
    Action<MainWindowViewModel> execute,
    string shortcut = "")
{
    public string Title { get; } = title;
    public string Category { get; } = category;
    public string Shortcut { get; } = shortcut;
    public Action<MainWindowViewModel>? Execute { get; } = execute;

    public string SearchText => $"{Title} {Category} {Shortcut}";

    public int FuzzyScore(string query) => FuzzyMatcher.Score(SearchText, query);
}
