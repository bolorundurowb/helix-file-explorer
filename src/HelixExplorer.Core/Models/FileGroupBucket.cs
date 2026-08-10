namespace HelixExplorer.Core.Models;

/// <summary>
/// A grouping bucket for one entry. <paramref name="Key"/> is stable across sessions and is what
/// gets persisted in collapsed-group state, so it must never be derived from localised text.
/// </summary>
/// <param name="Key">Stable, persistable identifier (e.g. <c>modified_today</c>).</param>
/// <param name="DisplayName">Localised header text.</param>
/// <param name="Order">Fixed position of the bucket relative to other buckets of the same mode.</param>
public readonly record struct FileGroupBucket(string Key, string DisplayName, int Order);
