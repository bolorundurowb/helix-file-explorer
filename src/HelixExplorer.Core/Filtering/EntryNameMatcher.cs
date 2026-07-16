namespace HelixExplorer.Core.Filtering;

/// <summary>
/// Shared name/path matching for Filter and Search: glob when the query has metacharacters,
/// otherwise case-insensitive substring.
/// </summary>
public static class EntryNameMatcher
{
    public static bool Matches(ReadOnlySpan<char> name, ReadOnlySpan<char> query)
    {
        query = query.Trim();
        if (query.IsEmpty)
            return true;

        if (GlobMatcher.HasGlobMetacharacters(query))
            return GlobMatcher.IsMatch(name, query);

        return FileNameFilter.Matches(name, query);
    }

    public static bool Matches(string name, string? query)
        => Matches(name.AsSpan(), (query ?? string.Empty).AsSpan());
}
