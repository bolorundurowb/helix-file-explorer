using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can swap its whole contents under a single
/// <see cref="NotifyCollectionChangedAction.Reset"/>.
/// </summary>
/// <remarks>
/// Avalonia's DataGrid ignores <see cref="NotifyCollectionChangedAction.Move"/>, so a re-sort delivered as
/// moves leaves rows in their old order. One Reset is also far cheaper for listing controls than a Clear
/// followed by one Add notification per entry.
/// </remarks>
public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs ResetArgs = new(NotifyCollectionChangedAction.Reset);

    public void ResetTo(IReadOnlyList<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        if (Items is List<T> list)
            list.EnsureCapacity(items.Count);

        for (var i = 0; i < items.Count; i++)
            Items.Add(items[i]);

        OnPropertyChanged(CountChanged);
        OnPropertyChanged(IndexerChanged);
        OnCollectionChanged(ResetArgs);
    }
}
