using System.Buffers;
using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Filtering;

/// <summary>
/// SIMD first-char probe via <see cref="SearchValues{T}"/>; never allocates via <c>ToLower()</c>.
/// </summary>
public static class FileNameFilter
{
    public static bool Matches(ReadOnlySpan<char> name, ReadOnlySpan<char> query)
    {
        query = query.Trim();
        if (query.IsEmpty)
            return true;

        if (GlobMatcher.HasGlobMetacharacters(query))
            return GlobMatcher.IsMatch(name, query);

        var probe = CreateCaseInsensitiveProbe(query[0]);
        if (query.Length == 1)
            return name.ContainsAny(probe);

        // First-char miss is the common case; skip full Contains to keep Ctrl+F cheap on large lists.
        if (!name.ContainsAny(probe))
            return false;

        return name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// Recycle Bin rows keep their pre-delete location in <see cref="FileSystemEntry.OriginalPath"/>;
    /// matching it lets users find items by the folder they were deleted from.
    /// </remarks>
    public static bool Matches(in FileSystemEntry entry, string? query)
    {
        var needle = (query ?? string.Empty).AsSpan();
        return Matches(entry.Name.AsSpan(), needle)
               || (entry.OriginalPath is { Length: > 0 } original && Matches(original.AsSpan(), needle));
    }

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

        var useGlob = GlobMatcher.HasGlobMetacharacters(trimmed);
        SearchValues<char>? probe = useGlob ? null : CreateCaseInsensitiveProbe(trimmed[0]);
        var isSingleChar = !useGlob && trimmed.Length == 1;
        var needle = trimmed.AsSpan();

        for (var i = 0; i < source.Count; i++)
        {
            var entry = source[i];
            if (MatchesPrepared(entry.Name.AsSpan(), needle, useGlob, probe, isSingleChar)
                || (entry.OriginalPath is { Length: > 0 } original
                    && MatchesPrepared(original.AsSpan(), needle, useGlob, probe, isSingleChar)))
            {
                destination.Add(entry);
            }
        }

        return destination.Count;
    }

    private static bool MatchesPrepared(
        ReadOnlySpan<char> text,
        ReadOnlySpan<char> needle,
        bool useGlob,
        SearchValues<char>? probe,
        bool isSingleChar)
    {
        if (useGlob)
            return GlobMatcher.IsMatch(text, needle);

        if (probe is not null && !text.ContainsAny(probe))
            return false;

        return isSingleChar || text.Contains(needle, StringComparison.OrdinalIgnoreCase);
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
