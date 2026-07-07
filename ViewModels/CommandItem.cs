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
}