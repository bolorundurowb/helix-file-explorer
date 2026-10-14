using System.Globalization;
using HelixExplorer.Core.Formatting;
using HelixExplorer.Core.Git;

namespace HelixExplorer.ViewModels.Pane;

public enum PaneContentState
{
    Ready,
    Empty,
    NoMatches,
    AccessDenied,
    Unavailable
}

/// <summary>
/// One file or folder in a selection, reduced to the fields the summary needs.
/// Directories and negative sizes have no known byte length.
/// </summary>
public readonly record struct SelectionSizePart(bool IsDirectory, long SizeBytes);

public readonly record struct PaneContentCopy(string Title, string? Detail, bool ShowClearFilter, bool ShowRetry);

/// <summary>
/// Pure pane copy: content-state choice, status summaries, git badges, and modified dates.
/// Kept off the view so the rules can be tested without a window.
/// </summary>
public static class PanePresentation
{
    public static PaneContentState ResolveContentState(
        bool isHome,
        bool hasFreshListing,
        bool hasFault,
        bool accessDenied,
        int itemCount,
        bool filterOrSearchActive)
    {
        if (isHome)
            return PaneContentState.Ready;

        if (hasFault)
            return accessDenied ? PaneContentState.AccessDenied : PaneContentState.Unavailable;

        // A navigation clears the rows before the refresh returns. That is not an empty folder.
        if (!hasFreshListing)
            return PaneContentState.Ready;

        if (itemCount == 0 && filterOrSearchActive)
            return PaneContentState.NoMatches;

        if (itemCount == 0)
            return PaneContentState.Empty;

        return PaneContentState.Ready;
    }

    public static PaneContentCopy DescribeContent(PaneContentState state, string? filterText, string? faultMessage)
    {
        switch (state)
        {
            case PaneContentState.Empty:
                return new PaneContentCopy("This folder is empty", null, false, false);
            case PaneContentState.NoMatches:
                var title = string.IsNullOrWhiteSpace(filterText)
                    ? "No items match …"
                    : $"No items match \"{filterText.Trim()}\"";
                return new PaneContentCopy(title, null, true, false);
            case PaneContentState.AccessDenied:
                return new PaneContentCopy(
                    "Access denied",
                    DetailUnlessSame("Access denied", faultMessage),
                    false,
                    true);
            case PaneContentState.Unavailable:
                return new PaneContentCopy(
                    "This location is unavailable",
                    string.IsNullOrWhiteSpace(faultMessage) ? null : faultMessage,
                    false,
                    true);
            default:
                return new PaneContentCopy(string.Empty, null, false, false);
        }
    }

    public static string ItemCountText(int count)
        => count == 1 ? "1 item" : $"{count} items";

    /// <summary>
    /// Null when nothing is selected. Size is appended only when at least one file has a known length.
    /// Folders do not contribute a fake 0 B.
    /// </summary>
    public static string? SelectionSummary(IReadOnlyList<SelectionSizePart> selected)
    {
        if (selected.Count == 0)
            return null;

        var label = selected.Count == 1 ? "1 selected" : $"{selected.Count} selected";
        long bytes = 0;
        var anyKnown = false;
        foreach (var part in selected)
        {
            if (part.IsDirectory || part.SizeBytes < 0)
                continue;

            anyKnown = true;
            bytes += part.SizeBytes;
        }

        return anyKnown ? $"{label} | {FileSizeFormatter.FormatBinary(bytes)}" : label;
    }

    public static string FormatAutomationName(string? displayName, string? typeLabel)
    {
        var name = displayName ?? string.Empty;
        var type = string.IsNullOrWhiteSpace(typeLabel) ? "Item" : typeLabel;
        return $"{name}, {type}";
    }

    public static string GitLetter(GitFileStatus status) => status switch
    {
        GitFileStatus.Modified => "M",
        GitFileStatus.AddedOrStaged => "A",
        GitFileStatus.Untracked => "U",
        GitFileStatus.Conflict => "!",
        _ => string.Empty
    };

    public static string? GitTooltip(GitFileStatus status) => status switch
    {
        GitFileStatus.Modified => "Modified, not staged",
        GitFileStatus.AddedOrStaged => "Staged",
        GitFileStatus.Untracked => "Untracked",
        GitFileStatus.Conflict => "Conflict",
        _ => null
    };

    /// <summary>
    /// Locale <c>g</c> for older instants. Within 24 hours the cell reads "Today" plus the locale short time.
    /// </summary>
    public static string FormatModified(DateTime modifiedUtc, DateTime nowUtc, TimeZoneInfo zone, CultureInfo culture)
    {
        if (modifiedUtc == DateTime.MinValue)
            return string.Empty;

        var local = ToZone(modifiedUtc, zone);
        var now = ToZone(nowUtc, zone);
        var delta = now - local;
        if (delta <= TimeSpan.FromHours(24) && delta >= TimeSpan.FromHours(-24))
            return $"Today {local.ToString("t", culture)}";

        return local.ToString("g", culture);
    }

    public static string FormatModifiedTooltip(DateTime modifiedUtc, TimeZoneInfo zone, CultureInfo culture)
    {
        if (modifiedUtc == DateTime.MinValue)
            return string.Empty;

        return ToZone(modifiedUtc, zone).ToString("F", culture);
    }

    private static string? DetailUnlessSame(string title, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        return string.Equals(message.Trim(), title, StringComparison.Ordinal)
            ? null
            : message;
    }

    private static DateTime ToZone(DateTime value, TimeZoneInfo zone)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
    }
}
