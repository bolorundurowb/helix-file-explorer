using CommunityToolkit.Mvvm.ComponentModel;

namespace HelixExplorer.ViewModels;

/// <summary>
/// A collapsible band in the Grid presentation collection. Headers are pooled by
/// <see cref="Key"/> across rebuilds so collapse state and container identity survive re-sorts.
/// </summary>
public sealed partial class GroupHeaderViewModel(string key, string title) : ObservableObject
{
    public string Key { get; } = key;

    public string Title { get; } = title;

    public bool IsExpanded => !IsCollapsed;

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    private bool _isCollapsed;
}
