using HelixExplorer.Core.FileSystem.Undo;

namespace HelixExplorer.Core.Tests;

public class RecycleBinMatcherTests
{
    private static readonly DateTime BatchStart = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Match_PairsSourceToItsBinEntry()
    {
        var changes = RecycleBinMatcher.Match(
            [@"C:\work\report.docx"],
            [new RecycleBinEntry(@"C:\$RECYCLE.BIN\S-1-5-21\$RABCDEF", @"C:\work\report.docx", BatchStart.AddSeconds(1))],
            BatchStart);

        changes.Count.Must().Be(1);
        changes[0].SourcePath.Must().Be(@"C:\work\report.docx");
        changes[0].DestinationPath.Must().Be(@"C:\work\report.docx");
        changes[0].RecycleItemPath.Must().Be(@"C:\$RECYCLE.BIN\S-1-5-21\$RABCDEF");
    }

    [Fact]
    public void Match_IgnoresEntriesDeletedBeforeTheBatch()
    {
        // A previous delete of the same path left an older entry in the bin; matching it would make
        // undo restore the wrong version of the file.
        var changes = RecycleBinMatcher.Match(
            [@"C:\work\notes.txt"],
            [new RecycleBinEntry(@"C:\$RECYCLE.BIN\S-1-5-21\$ROLD", @"C:\work\notes.txt", BatchStart.AddHours(-3))],
            BatchStart);

        changes.Count.Must().Be(1);
        changes[0].RecycleItemPath.Must().BeNull();
    }

    [Fact]
    public void Match_HandsOutOneEntryPerSourceForDuplicatePaths()
    {
        // The same original path can legitimately appear twice when an item is deleted, recreated, and
        // deleted again within one batch. Each source must claim a different bin entry.
        var changes = RecycleBinMatcher.Match(
            [@"C:\work\dup.txt", @"C:\work\dup.txt"],
            [
                new RecycleBinEntry(@"C:\$RECYCLE.BIN\S-1-5-21\$RSECOND", @"C:\work\dup.txt", BatchStart.AddSeconds(9)),
                new RecycleBinEntry(@"C:\$RECYCLE.BIN\S-1-5-21\$RFIRST", @"C:\work\dup.txt", BatchStart.AddSeconds(2))
            ],
            BatchStart);

        changes.Count.Must().Be(2);
        changes[0].RecycleItemPath.Must().Be(@"C:\$RECYCLE.BIN\S-1-5-21\$RFIRST");
        changes[1].RecycleItemPath.Must().Be(@"C:\$RECYCLE.BIN\S-1-5-21\$RSECOND");
    }

    [Fact]
    public void Match_LeavesRecycleItemNullWhenNoEntryFound()
    {
        var changes = RecycleBinMatcher.Match(
            [@"C:\work\a.txt", @"C:\work\b.txt"],
            [new RecycleBinEntry(@"C:\$RECYCLE.BIN\S-1-5-21\$RA", @"C:\work\a.txt", BatchStart.AddSeconds(1))],
            BatchStart);

        changes.Count.Must().Be(2);
        changes[0].RecycleItemPath.Must().NotBeNull();
        changes[1].RecycleItemPath.Must().BeNull();
    }

    [Fact]
    public void Match_IsCaseInsensitiveOnOriginalPath()
    {
        // The shell reports parsing names with whatever casing the filesystem stored.
        var changes = RecycleBinMatcher.Match(
            [@"C:\Work\Report.docx"],
            [new RecycleBinEntry(@"C:\$RECYCLE.BIN\S-1-5-21\$RX", @"c:\work\report.docx", BatchStart.AddSeconds(1))],
            BatchStart);

        changes[0].RecycleItemPath.Must().Be(@"C:\$RECYCLE.BIN\S-1-5-21\$RX");
    }

    [Fact]
    public void Match_ReturnsEmptyForNoSources()
        => RecycleBinMatcher.Match([], [], BatchStart).Count.Must().Be(0);
}
