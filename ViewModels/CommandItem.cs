using CommunityToolkit.Mvvm.ComponentModel;

namespace HelixExplorer.ViewModels;

/// <summary>Item in the command palette overlay.</summary>
public sealed partial class CommandItem : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string _shortcut = string.Empty;

    public Action<MainWindowViewModel>? Execute { get; init; }

    public string SearchText => $"{Title} {Category} {Shortcut}";

    public CommandItem() { }

    public CommandItem(string title, string category, Action<MainWindowViewModel> execute, string shortcut = "")
    {
        Title = title;
        Category = category;
        Shortcut = shortcut;
        Execute = execute;
    }

    /// <summary>
    /// Subsequence fuzzy match. Returns a score (higher = better) when every character of
    /// <paramref name="query"/> appears in order within the target, rewarding consecutive
    /// and word-boundary matches. Returns a negative value when there is no match.
    /// </summary>
    public int FuzzyScore(string query) => FuzzyScore(SearchText, query);

    public static int FuzzyScore(string target, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(target)) return -1;

        int score = 0;
        int ti = 0;
        int consecutive = 0;
        bool prevWasSeparator = true;

        for (int qi = 0; qi < query.Length; qi++)
        {
            char qc = char.ToLowerInvariant(query[qi]);
            bool matched = false;
            while (ti < target.Length)
            {
                char tc = target[ti];
                bool boundary = prevWasSeparator;
                prevWasSeparator = tc is ' ' or '/' or '\\' or '_' or '-' or '.';
                if (char.ToLowerInvariant(tc) == qc)
                {
                    score += 1 + consecutive * 2 + (boundary ? 3 : 0);
                    consecutive++;
                    ti++;
                    matched = true;
                    break;
                }
                consecutive = 0;
                ti++;
            }
            if (!matched) return -1;
        }
        return score;
    }
}
