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

    public static bool Matches(in FileSystemEntry entry, string? query)
        => Matches(entry.Name.AsSpan(), (query ?? string.Empty).AsSpan());

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
            var name = entry.Name.AsSpan();

            if (useGlob)
            {
                if (GlobMatcher.IsMatch(name, needle))
                    destination.Add(entry);
                continue;
            }

            if (probe is not null && !name.ContainsAny(probe))
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
