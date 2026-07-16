using System.Collections.ObjectModel;

namespace HelixExplorer.ViewModels.Pane;

public sealed class PaneSelectionModel
{
    private int _anchorIndex = -1;

    public ObservableCollection<EntryItemViewModel> SelectedEntries { get; } = new();

    public EntryItemViewModel? SelectedEntry { get; private set; }

    public int SelectedCount => SelectedEntries.Count;

    public event EventHandler? SelectionChanged;

    public void UpdateSelection(IList<EntryItemViewModel> entries, IReadOnlyList<EntryItemViewModel> allEntries)
    {
        ReplaceSelection(entries, allEntries, preferredSingle: null);
    }

    /// <summary>
    /// Replaces the selection in one shot and raises <see cref="SelectionChanged"/> once.
    /// Use for restore-after-sort so callers can sync entry flags a single time.
    /// </summary>
    public void ReplaceSelection(
        IEnumerable<EntryItemViewModel> entries,
        IReadOnlyList<EntryItemViewModel> allEntries,
        EntryItemViewModel? preferredSingle)
    {
        SelectedEntries.Clear();
        foreach (var entry in entries)
            SelectedEntries.Add(entry);

        SelectedEntry = preferredSingle
            ?? (SelectedEntries.Count == 1 ? SelectedEntries[0] : null);
        _anchorIndex = SelectedEntry is not null ? IndexOf(allEntries, SelectedEntry) : -1;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectSingle(EntryItemViewModel entry, IReadOnlyList<EntryItemViewModel> allEntries)
    {
        UpdateSelection([entry], allEntries);
    }

    public void Toggle(EntryItemViewModel entry, IReadOnlyList<EntryItemViewModel> allEntries)
    {
        var removed = SelectedEntries.Contains(entry);
        if (removed)
            SelectedEntries.Remove(entry);
        else
            SelectedEntries.Add(entry);

        SelectedEntry = SelectedEntries.Count == 1 ? SelectedEntries[0] : null;

        if (removed)
        {
            // Toggling an entry OFF must not leave the anchor on a now-unselected row; otherwise a
            // subsequent Shift+Click would extend the range from the wrong origin. Re-anchor to the
            // first still-selected entry (or clear the anchor when nothing remains selected).
            _anchorIndex = SelectedEntries.Count == 0
                ? -1
                : LowestIndexAmong(allEntries, SelectedEntries);
        }
        else
        {
            _anchorIndex = IndexOf(allEntries, entry);
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectRange(EntryItemViewModel target, IReadOnlyList<EntryItemViewModel> allEntries)
    {
        var targetIndex = IndexOf(allEntries, target);
        if (targetIndex < 0)
        {
            SelectSingle(target, allEntries);
            return;
        }

        if (_anchorIndex < 0)
            _anchorIndex = targetIndex;

        var start = Math.Min(_anchorIndex, targetIndex);
        var end = Math.Max(_anchorIndex, targetIndex);
        var range = new List<EntryItemViewModel>();
        for (var i = start; i <= end; i++)
            range.Add(allEntries[i]);

        UpdateSelection(range, allEntries);
        SelectedEntry = target;
    }

    public void SelectByBounds(
        IReadOnlyList<EntryItemViewModel> hits,
        IReadOnlyList<EntryItemViewModel> allEntries,
        bool additive)
    {
        if (!additive)
        {
            UpdateSelection(hits.ToList(), allEntries);
            return;
        }

        foreach (var hit in hits)
        {
            if (!SelectedEntries.Contains(hit))
                SelectedEntries.Add(hit);
        }

        SelectedEntry = SelectedEntries.Count == 1 ? SelectedEntries[0] : null;
        if (SelectedEntry is not null)
            _anchorIndex = IndexOf(allEntries, SelectedEntry);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll(IReadOnlyList<EntryItemViewModel> allEntries)
    {
        UpdateSelection(allEntries.ToList(), allEntries);
    }

    public void Clear()
    {
        SelectedEntries.Clear();
        SelectedEntry = null;
        _anchorIndex = -1;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the lowest index in <paramref name="allEntries"/> among the given
    /// <paramref name="selected"/> entries, giving visual top-of-range semantics regardless of
    /// the order the user selected items.
    /// </summary>
    private static int LowestIndexAmong(
        IReadOnlyList<EntryItemViewModel> allEntries,
        IEnumerable<EntryItemViewModel> selected)
    {
        var min = int.MaxValue;
        foreach (var entry in selected)
        {
            var idx = IndexOf(allEntries, entry);
            if (idx >= 0 && idx < min)
                min = idx;
        }

        return min == int.MaxValue ? -1 : min;
    }

    private static int IndexOf(IReadOnlyList<EntryItemViewModel> entries, EntryItemViewModel entry)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (ReferenceEquals(entries[i], entry))
                return i;
        }

        return -1;
    }
}
