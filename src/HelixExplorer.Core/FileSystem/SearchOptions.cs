using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.FileSystem;

/// <summary>Bounds for a recursive search so a large tree cannot produce unbounded work or results.</summary>
public sealed record SearchOptions
{
    public int MaxResults { get; init; } = 500;

    public int MaxDepth { get; init; } = 16;

    public bool IncludeHiddenAndSystem { get; init; }

    public static readonly SearchOptions Default = new();
}

public sealed record SearchResult(IReadOnlyList<FileSystemEntry> Entries, bool Capped)
{
    public static readonly SearchResult Empty = new(Array.Empty<FileSystemEntry>(), false);
}
