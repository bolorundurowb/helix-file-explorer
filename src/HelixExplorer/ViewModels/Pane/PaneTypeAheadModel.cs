namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Incremental "type to select" state for a pane listing: pressing letters moves the cursor to the
/// first matching entry, repeating one letter cycles matches, and typing more letters narrows within
/// the current match until the buffer expires. Pure logic — the caller supplies the listing and the
/// current cursor — so the behaviour is unit-testable without a view.
/// </summary>
public sealed class PaneTypeAheadModel
{
    /// <summary>Idle gap after which typing starts a new search instead of extending the previous one.</summary>
    public static readonly TimeSpan BufferTimeout = TimeSpan.FromSeconds(1);

    private string _buffer = string.Empty;
    private DateTime _lastInputUtc;

    public string Buffer => _buffer;

    public void Reset()
    {
        _buffer = string.Empty;
        _lastInputUtc = default;
    }

    /// <summary>
    /// Index in <paramref name="entries"/> that should become the selection, or -1 when the input is
    /// not searchable or nothing matches (in which case the caller leaves the selection alone).
    /// </summary>
    /// <param name="currentIndex">Index of the current cursor, or -1 when nothing is selected.</param>
    public int Resolve(
        string? text,
        IReadOnlyList<EntryItemViewModel> entries,
        int currentIndex,
        DateTime nowUtc)
    {
        if (!IsSearchableText(text) || entries.Count == 0)
            return -1;

        var expired = _buffer.Length == 0 || nowUtc - _lastInputUtc > BufferTimeout;
        var buffer = expired ? text! : _buffer + text;

        // Repeating a single character cycles through matches (Explorer behaviour); anything longer
        // narrows the search and may keep the current entry selected.
        var cycling = IsSingleRepeatedCharacter(buffer);
        var prefix = cycling ? buffer[..1] : buffer;
        var startIndex = cycling || expired ? currentIndex + 1 : Math.Max(currentIndex, 0);

        var match = FindPrefixMatch(entries, prefix, startIndex);
        if (match < 0)
        {
            // Keep the previous buffer so the next keystroke continues the search that was working.
            return -1;
        }

        _buffer = buffer;
        _lastInputUtc = nowUtc;
        return match;
    }

    private static int FindPrefixMatch(
        IReadOnlyList<EntryItemViewModel> entries,
        string prefix,
        int startIndex)
    {
        if (startIndex < 0)
            startIndex = 0;

        // Wrap so the search covers the whole listing regardless of where the cursor sits.
        for (var offset = 0; offset < entries.Count; offset++)
        {
            var index = (startIndex + offset) % entries.Count;
            if (Matches(entries[index], prefix))
                return index;
        }

        return -1;
    }

    /// <summary>
    /// Matches what the user can read first (extensions may be hidden), then the real name, so typing
    /// works for both presentations. Culture-aware so accented and locale-specific names behave.
    /// </summary>
    private static bool Matches(EntryItemViewModel entry, string prefix)
        => entry.DisplayName.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)
           || entry.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase);

    private static bool IsSearchableText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var character in text)
        {
            // Control characters come from shortcut combinations, not from typing a name.
            if (char.IsControl(character))
                return false;
        }

        return true;
    }

    private static bool IsSingleRepeatedCharacter(string buffer)
    {
        if (buffer.Length < 2)
            return false;

        foreach (var character in buffer)
        {
            if (char.ToLowerInvariant(character) != char.ToLowerInvariant(buffer[0]))
                return false;
        }

        return true;
    }
}
