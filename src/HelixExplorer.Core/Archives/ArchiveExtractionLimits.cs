namespace HelixExplorer.Core.Archives;

public sealed record ArchiveExtractionLimits(
    int MaxEntryCount,
    long MaxEntryUncompressedBytes,
    long MaxTotalUncompressedBytes)
{
    public static ArchiveExtractionLimits Default { get; } = new(
        MaxEntryCount: 100_000,
        MaxEntryUncompressedBytes: 2L * 1024 * 1024 * 1024,
        MaxTotalUncompressedBytes: 8L * 1024 * 1024 * 1024);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxEntryUncompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTotalUncompressedBytes);
    }
}

/// <summary>
/// Thrown when an archive would exceed <see cref="ArchiveExtractionLimits"/>. Distinct from a
/// corrupt-archive failure: the archive is readable, extraction is refused on policy grounds, so
/// callers must surface it instead of folding it into the generic "extraction failed" log path.
/// </summary>
public sealed class ArchiveExtractionLimitException(string message) : IOException(message);
