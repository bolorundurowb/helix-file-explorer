using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

/// <summary>Bounds for a recursive search so a large tree cannot produce unbounded work or results.</summary>
public sealed record SearchOptions
{
    /// <summary>Maximum number of matches to return before the search stops and reports it was capped.</summary>
    public int MaxResults { get; init; } = 500;

    /// <summary>Maximum directory depth to recurse below the search root (root itself is depth 0).</summary>
    public int MaxDepth { get; init; } = 16;

    /// <summary>Whether to descend into and match hidden/system entries.</summary>
    public bool IncludeHiddenAndSystem { get; init; }

    public static readonly SearchOptions Default = new();
}

/// <summary>Result of a bounded recursive search.</summary>
public sealed record SearchResult(IReadOnlyList<FileSystemEntry> Entries, bool Capped)
{
    public static readonly SearchResult Empty = new(Array.Empty<FileSystemEntry>(), false);
}
