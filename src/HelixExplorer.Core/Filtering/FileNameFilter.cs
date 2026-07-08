using System.Buffers;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Filtering;

/// <summary>
/// Low-allocation, case-insensitive substring matching for the in-view quick filter (Ctrl+F).
/// Hot path uses <see cref="SearchValues{T}"/> for a SIMD first-character probe, then confirms
/// with span <c>Contains</c> — never allocates via <c>ToLower()</c>.
/// </summary>
public static class FileNameFilter
{
    /// <summary>True when <paramref name="query"/> is blank or a case-insensitive substring of <paramref name="name"/>.</summary>
    public static bool Matches(ReadOnlySpan<char> name, ReadOnlySpan<char> query)
    {
        query = query.Trim();
        if (query.IsEmpty)
            return true;

        var probe = CreateCaseInsensitiveProbe(query[0]);
        if (query.Length == 1)
            return name.ContainsAny(probe);

        // Reject quickly when the first character never appears, then confirm the full substring.
        if (!name.ContainsAny(probe))
            return false;

        return name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the entry's name matches the query.</summary>
    public static bool Matches(in FileSystemEntry entry, string? query)
        => Matches(entry.Name.AsSpan(), (query ?? string.Empty).AsSpan());

    /// <summary>
    /// Copies matching entries from <paramref name="source"/> into <paramref name="destination"/>,
    /// preserving order. Returns the number of matches written.
    /// </summary>
    public static int Apply(
        IReadOnlyList<FileSystemEntry> source,
        string? query,
        List<FileSystemEntry> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        destination.Clear();

        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            destination.AddRange(source);
            return destination.Count;
        }

        var probe = CreateCaseInsensitiveProbe(trimmed[0]);
        var isSingleChar = trimmed.Length == 1;
        var needle = trimmed.AsSpan();

        for (var i = 0; i < source.Count; i++)
        {
            var entry = source[i];
            var name = entry.Name.AsSpan();

            if (!name.ContainsAny(probe))
                continue;

            if (isSingleChar || name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                destination.Add(entry);
        }

        return destination.Count;
    }

    private static SearchValues<char> CreateCaseInsensitiveProbe(char c)
    {
        var lower = char.ToLowerInvariant(c);
        var upper = char.ToUpperInvariant(c);
        return lower == upper
            ? SearchValues.Create([lower])
            : SearchValues.Create([lower, upper]);
    }
}
