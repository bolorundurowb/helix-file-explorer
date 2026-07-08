using HelixExplorer.Core.Search;

namespace HelixExplorer.ViewModels;

public sealed class CommandItem
{
    public CommandItem(
        string title,
        string category,
        Action<MainWindowViewModel> execute,
        string shortcut = "")
    {
        Title = title;
        Category = category;
        Shortcut = shortcut;
        Execute = execute;
    }

    public string Title { get; }
    public string Category { get; }
    public string Shortcut { get; }
    public Action<MainWindowViewModel>? Execute { get; }

    public string SearchText => $"{Title} {Category} {Shortcut}";

    public int FuzzyScore(string query) => FuzzyMatcher.Score(SearchText, query);
}
