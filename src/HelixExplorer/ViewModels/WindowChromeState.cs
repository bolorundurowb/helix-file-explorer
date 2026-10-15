using HelixExplorer.Core.Formatting;
using HelixExplorer.Core.Models;

namespace HelixExplorer.ViewModels;

/// <summary>
/// Path and view mode for a browser tab that was closed, so it can be reopened.
/// </summary>
public sealed record ClosedTabSnapshot(string Path, LayoutMode ViewMode);

/// <summary>
/// Most-recently-closed tabs. Capped so a long session cannot grow the stack without bound.
/// </summary>
public sealed class ClosedTabHistory
{
    public const int Capacity = 10;

    private readonly List<ClosedTabSnapshot> _items = new();

    public int Count => _items.Count;

    public void Push(ClosedTabSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Path))
            return;

        _items.Insert(0, snapshot);
        if (_items.Count > Capacity)
            _items.RemoveAt(_items.Count - 1);
    }

    public ClosedTabSnapshot? TryPop()
    {
        if (_items.Count == 0)
            return null;

        var item = _items[0];
        _items.RemoveAt(0);
        return item;
    }
}

/// <summary>
/// F6 order: sidebar, address, active pane, the other pane when split, then the status bar.
/// </summary>
public static class ChromeFocusCycle
{
    public const int Sidebar = 0;
    public const int Address = 1;
    public const int ActivePane = 2;
    public const int OtherPane = 3;
    public const int StatusBar = 4;
    public const int SlotCount = 5;

    public static int Next(int current, bool hasOtherPane)
    {
        var slot = current;
        for (var i = 0; i < SlotCount; i++)
        {
            slot++;
            if (slot >= SlotCount)
                slot = 0;

            if (slot == OtherPane && !hasOtherPane)
                continue;

            return slot;
        }

        return Sidebar;
    }
}

/// <summary>
/// Status-bar listing lines. Folders have no byte size, so they must not contribute "0 B".
/// </summary>
public static class ListingSummaryText
{
    public static string Items(int count)
        => count == 1 ? "1 item" : $"{count} items";

    public static string? Selection(int selectedCount, IEnumerable<EntryItemViewModel> selected)
    {
        if (selectedCount <= 0)
            return null;

        var label = selectedCount == 1 ? "1 selected" : $"{selectedCount} selected";
        long bytes = 0;
        var known = false;
        foreach (var entry in selected)
        {
            if (entry.IsDirectory || entry.SizeBytes < 0)
                continue;

            known = true;
            bytes += entry.SizeBytes;
        }

        return known
            ? $"{label} · {FileSizeFormatter.FormatBinary(bytes)}"
            : label;
    }
}

/// <summary>
/// Expanded width stays in the saved setting. Collapsed is a separate zero-width column
/// so the clamp that keeps the sidebar between 200 and 450 does not snap it back open.
/// </summary>
public static class SidebarColumnLayout
{
    public const double ExpandedMin = 200;
    public const double ExpandedMax = 450;

    public static (double Width, double MinWidth, double MaxWidth) Resolve(double expandedWidth, bool collapsed)
        => collapsed
            ? (0, 0, 0)
            : (expandedWidth, ExpandedMin, ExpandedMax);
}
