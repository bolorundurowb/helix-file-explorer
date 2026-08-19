using System.Collections.ObjectModel;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Sole owner of the pane selection: the <see cref="SelectedEntries"/> collection *and* each entry's
/// <see cref="EntryItemViewModel.IsSelected"/> flag. Every public operation touches only the entries
/// that actually enter or leave the selection and raises <see cref="SelectionChanged"/> exactly once,
/// so extending a selection stays O(delta) instead of re-publishing the whole set per keystroke.
/// </summary>
public sealed class PaneSelectionModel
{
    private readonly HashSet<EntryItemViewModel> _selected = new();
    private int _anchorIndex = -1;
    private int _rangeEndIndex = -1;

    /// <summary>
    /// True while <see cref="SelectedEntries"/> is exactly the contiguous span between
    /// <see cref="_anchorIndex"/> and <see cref="_rangeEndIndex"/>. Only then can a new range target be
    /// applied as a delta; every other mutation invalidates it.
    /// </summary>
    private bool _rangeValid;

    public ObservableCollection<EntryItemViewModel> SelectedEntries { get; } = new();

    public EntryItemViewModel? SelectedEntry { get; private set; }

    public int SelectedCount => SelectedEntries.Count;

    public event EventHandler? SelectionChanged;

    public void UpdateSelection(IList<EntryItemViewModel> entries, IReadOnlyList<EntryItemViewModel> allEntries)
    {
        ReplaceSelection(entries, allEntries, preferredSingle: null);
    }

    /// <summary>
    /// Makes the selection equal to <paramref name="entries"/>, applying only the difference against
    /// the current selection, and raises <see cref="SelectionChanged"/> once.
    /// </summary>
    public void ReplaceSelection(
        IEnumerable<EntryItemViewModel> entries,
        IReadOnlyList<EntryItemViewModel> allEntries,
        EntryItemViewModel? preferredSingle)
    {
        ApplyDesiredSelection(entries);

        SelectedEntry = preferredSingle
            ?? (SelectedEntries.Count == 1 ? SelectedEntries[0] : null);
        _anchorIndex = SelectedEntry is not null ? IndexOf(allEntries, SelectedEntry) : -1;
        _rangeValid = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies a selection change that the native list control has already made (for example the
    /// DataGrid extending with Shift+Down, which reports a single added row). Avoids rebuilding the
    /// whole selection from the control's <c>SelectedItems</c> on every keystroke.
    /// </summary>
    public void ApplyNativeDelta(
        IReadOnlyList<EntryItemViewModel> added,
        IReadOnlyList<EntryItemViewModel> removed,
        IReadOnlyList<EntryItemViewModel> allEntries)
    {
        var changed = false;

        foreach (var entry in removed)
            changed |= Deselect(entry);

        foreach (var entry in added)
            changed |= Select(entry);

        if (!changed)
            return;

        // The last added row is the control's active item (the Shift+Down cursor).
        var active = added.Count > 0 ? added[^1] : SelectedEntry;
        if (active is null || !_selected.Contains(active))
            active = SelectedEntries.Count == 1 ? SelectedEntries[0] : null;

        SelectedEntry = active;
        if (SelectedEntries.Count == 0)
            _anchorIndex = -1;
        else if (_anchorIndex < 0 || SelectedEntries.Count == 1)
            _anchorIndex = active is null ? -1 : IndexOf(allEntries, active);

        // The control owns the range origin in this path, so our own range delta cannot be trusted.
        _rangeValid = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectSingle(EntryItemViewModel entry, IReadOnlyList<EntryItemViewModel> allEntries)
    {
        UpdateSelection([entry], allEntries);
    }

    public void Toggle(EntryItemViewModel entry, IReadOnlyList<EntryItemViewModel> allEntries)
    {
        var removed = Deselect(entry);
        if (!removed)
            Select(entry);

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

        _rangeValid = false;
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

        if (_anchorIndex < 0 || _anchorIndex >= allEntries.Count)
            _anchorIndex = targetIndex;

        var start = Math.Min(_anchorIndex, targetIndex);
        var end = Math.Max(_anchorIndex, targetIndex);

        // Do not use ReplaceSelection here: a range can be extended and contracted repeatedly,
        // so its original anchor must survive while the target becomes the active item.
        if (_rangeValid && _rangeEndIndex >= 0 && _rangeEndIndex < allEntries.Count)
        {
            ApplyRangeDelta(
                allEntries,
                previousStart: Math.Min(_anchorIndex, _rangeEndIndex),
                previousEnd: Math.Max(_anchorIndex, _rangeEndIndex),
                start,
                end);
        }
        else
        {
            ReplaceWithRange(allEntries, start, end);
        }

        _rangeEndIndex = targetIndex;
        _rangeValid = true;
        SelectedEntry = target;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectByBounds(
        IReadOnlyList<EntryItemViewModel> hits,
        IReadOnlyList<EntryItemViewModel> allEntries,
        bool additive)
    {
        if (!additive)
        {
            UpdateSelection(hits.ToList(), allEntries);
            if (hits.Count > 0)
                _anchorIndex = LowestIndexAmong(allEntries, hits);
            return;
        }

        foreach (var hit in hits)
            Select(hit);

        SelectedEntry = SelectedEntries.Count == 1 ? SelectedEntries[0] : null;
        if (hits.Count > 0)
            _anchorIndex = LowestIndexAmong(allEntries, hits);
        _rangeValid = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll(IReadOnlyList<EntryItemViewModel> allEntries)
    {
        UpdateSelection(allEntries.ToList(), allEntries);
    }

    public void Invert(IReadOnlyList<EntryItemViewModel> allEntries)
    {
        var inverted = new List<EntryItemViewModel>(allEntries.Count);
        foreach (var entry in allEntries)
        {
            if (!_selected.Contains(entry))
                inverted.Add(entry);
        }

        UpdateSelection(inverted, allEntries);
    }

    public void Clear()
    {
        DeselectAll();
        SelectedEntry = null;
        _anchorIndex = -1;
        _rangeEndIndex = -1;
        _rangeValid = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Select(EntryItemViewModel entry)
    {
        if (!_selected.Add(entry))
            return false;

        SelectedEntries.Add(entry);
        entry.IsSelected = true;
        return true;
    }

    /// <summary>Adds at the front so a range extended upwards stays in listing order.</summary>
    private bool Prepend(EntryItemViewModel entry)
    {
        if (!_selected.Add(entry))
            return false;

        SelectedEntries.Insert(0, entry);
        entry.IsSelected = true;
        return true;
    }

    private bool Deselect(EntryItemViewModel entry)
    {
        if (!_selected.Remove(entry))
            return false;

        SelectedEntries.Remove(entry);
        entry.IsSelected = false;
        return true;
    }

    private void DeselectAll()
    {
        if (SelectedEntries.Count == 0)
            return;

        foreach (var entry in SelectedEntries)
            entry.IsSelected = false;

        SelectedEntries.Clear();
        _selected.Clear();
    }

    private void ApplyDesiredSelection(IEnumerable<EntryItemViewModel> desired)
    {
        // Copy when a caller hands us the live selection: the loops below mutate it as they go.
        var wanted = ReferenceEquals(desired, SelectedEntries)
            ? desired.ToList()
            : desired as IReadOnlyList<EntryItemViewModel> ?? desired.ToList();

        if (wanted.Count == 0)
        {
            DeselectAll();
            return;
        }

        var wantedSet = new HashSet<EntryItemViewModel>(wanted);
        for (var i = SelectedEntries.Count - 1; i >= 0; i--)
        {
            var entry = SelectedEntries[i];
            if (wantedSet.Contains(entry))
                continue;

            SelectedEntries.RemoveAt(i);
            _selected.Remove(entry);
            entry.IsSelected = false;
        }

        foreach (var entry in wanted)
            Select(entry);
    }

    private void ApplyRangeDelta(
        IReadOnlyList<EntryItemViewModel> allEntries,
        int previousStart,
        int previousEnd,
        int start,
        int end)
    {
        for (var i = previousStart; i < start && i <= previousEnd; i++)
            Deselect(allEntries[i]);

        for (var i = previousEnd; i > end && i >= previousStart; i--)
            Deselect(allEntries[i]);

        for (var i = previousStart - 1; i >= start; i--)
            Prepend(allEntries[i]);

        for (var i = previousEnd + 1; i <= end; i++)
            Select(allEntries[i]);
    }

    private void ReplaceWithRange(IReadOnlyList<EntryItemViewModel> allEntries, int start, int end)
    {
        DeselectAll();
        for (var i = start; i <= end; i++)
            Select(allEntries[i]);
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
