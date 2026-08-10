using HelixExplorer.Core.Grouping;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;

namespace HelixExplorer.Core.Tests;

public class FileGroupingNameBucketTests
{
    [Theory]
    [InlineData("apple", "name_a_h")]
    [InlineData("Hazel", "name_a_h")]
    [InlineData("index.md", "name_i_p")]
    [InlineData("photos", "name_i_p")]
    [InlineData("queue", "name_q_z")]
    [InlineData("Zebra", "name_q_z")]
    [InlineData("_hidden", "name_other")]
    [InlineData("3rd-party", "name_other")]
    [InlineData("!bang", "name_other")]
    [InlineData("émile", "name_other")]
    public void GetNameBucket_PlacesByFirstLetter(string name, string expectedKey)
    {
        FileGrouping.GetNameBucket(name).Key.Must().Be(expectedKey);
    }

    [Fact]
    public void GetNameBucket_EmptyAndNull_FallBackToOther()
    {
        FileGrouping.GetNameBucket(string.Empty).Key.Must().Be("name_other");
        FileGrouping.GetNameBucket(null).Key.Must().Be("name_other");
    }
}

public class FileGroupingModifiedBucketTests
{
    // A Wednesday, so "earlier this week" and "last week" are both reachable from the same anchor.
    private static readonly DateTime Now = new(2026, 8, 5, 13, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-08-05T00:00:00", "modified_today")]
    [InlineData("2026-08-05T23:59:59", "modified_today")]
    [InlineData("2026-08-06T09:00:00", "modified_today")]
    [InlineData("2026-08-04T22:00:00", "modified_yesterday")]
    [InlineData("2026-08-03T00:00:00", "modified_earlier_this_week")]
    [InlineData("2026-08-02T23:59:59", "modified_last_week")]
    [InlineData("2026-07-27T00:00:00", "modified_last_week")]
    [InlineData("2026-07-26T23:59:59", "modified_last_month")]
    [InlineData("2026-07-01T00:00:00", "modified_last_month")]
    [InlineData("2026-06-30T23:59:59", "modified_long_ago")]
    [InlineData("1980-01-01T00:00:00", "modified_long_ago")]
    public void GetModifiedBucket_RespectsBoundaries(string modified, string expectedKey)
    {
        var value = DateTime.Parse(modified, System.Globalization.CultureInfo.InvariantCulture);
        FileGrouping.GetModifiedBucket(value, Now).Key.Must().Be(expectedKey);
    }

    [Theory]
    // Anchored on Thursday 20 Aug 2026: week starts the 17th, last week the 10th, month the 1st,
    // so "earlier this month" is the only bucket that can hold the 5th.
    [InlineData("2026-08-19T00:00:00", "modified_yesterday")]
    [InlineData("2026-08-17T00:00:00", "modified_earlier_this_week")]
    [InlineData("2026-08-10T00:00:00", "modified_last_week")]
    [InlineData("2026-08-09T23:59:59", "modified_earlier_this_month")]
    [InlineData("2026-08-01T00:00:00", "modified_earlier_this_month")]
    [InlineData("2026-07-31T23:59:59", "modified_last_month")]
    public void GetModifiedBucket_SeparatesThisMonthFromLastMonth(string modified, string expectedKey)
    {
        var now = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
        var value = DateTime.Parse(modified, System.Globalization.CultureInfo.InvariantCulture);
        FileGrouping.GetModifiedBucket(value, now).Key.Must().Be(expectedKey);
    }

    [Fact]
    public void GetModifiedBucket_OnMonday_YesterdayWinsOverLastWeek()
    {
        var monday = new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc);
        var sunday = new DateTime(2026, 8, 2, 20, 0, 0, DateTimeKind.Utc);
        FileGrouping.GetModifiedBucket(sunday, monday).Key.Must().Be("modified_yesterday");
    }
}

public class FileGroupingSizeBucketTests
{
    [Theory]
    [InlineData(0, "size_tiny")]
    [InlineData(16 * 1024L - 1, "size_tiny")]
    [InlineData(16 * 1024L, "size_small")]
    [InlineData(1024L * 1024L - 1, "size_small")]
    [InlineData(1024L * 1024L, "size_medium")]
    [InlineData(128L * 1024L * 1024L - 1, "size_medium")]
    [InlineData(128L * 1024L * 1024L, "size_large")]
    [InlineData(1024L * 1024L * 1024L - 1, "size_large")]
    [InlineData(1024L * 1024L * 1024L, "size_huge")]
    [InlineData(4L * 1024L * 1024L * 1024L - 1, "size_huge")]
    [InlineData(4L * 1024L * 1024L * 1024L, "size_gigantic")]
    public void GetSizeBucket_RespectsBoundaries(long sizeBytes, string expectedKey)
    {
        FileGrouping.GetSizeBucket(isDirectory: false, sizeBytes).Key.Must().Be(expectedKey);
    }

    [Fact]
    public void GetSizeBucket_Directories_LeadRegardlessOfReportedSize()
    {
        var bucket = FileGrouping.GetSizeBucket(isDirectory: true, 5L * 1024 * 1024 * 1024);
        bucket.Key.Must().Be("size_folder");
        bucket.Order.Must().Be(0);
    }
}

public class FileGroupingTypeBucketTests
{
    [Fact]
    public void GetTypeBucket_Folders_LeadWithOrderZero()
    {
        var folder = new FileSystemEntry(@"C:\docs", "docs", true, 0, DateTime.UtcNow, string.Empty);
        var bucket = FileGrouping.GetTypeBucket(in folder);
        bucket.Key.Must().Be("type_folder");
        bucket.Order.Must().Be(0);
    }

    [Fact]
    public void GetTypeBucket_ExtensionlessFile_UsesGenericFileBucket()
    {
        var file = new FileSystemEntry(@"C:\LICENSE", "LICENSE", false, 10, DateTime.UtcNow, string.Empty);
        FileGrouping.GetTypeBucket(in file).Key.Must().Be("type_file");
    }

    [Fact]
    public void GetTypeBucket_KeyIsCaseStableAcrossExtensionCasing()
    {
        var lower = new FileSystemEntry(@"C:\a.txt", "a.txt", false, 1, DateTime.UtcNow, ".txt");
        var upper = new FileSystemEntry(@"C:\b.TXT", "b.TXT", false, 1, DateTime.UtcNow, ".TXT");
        FileGrouping.GetTypeBucket(in lower).Key.Must().Be(FileGrouping.GetTypeBucket(in upper).Key);
        FileGrouping.GetTypeBucket(in upper).DisplayName.Must().Be("TXT File");
    }

    [Fact]
    public void GetBucket_None_ReturnsUngrouped()
    {
        var file = new FileSystemEntry(@"C:\a.txt", "a.txt", false, 1, DateTime.UtcNow, ".txt");
        FileGrouping.GetBucket(in file, GroupByMode.None, DateTime.UtcNow).Must().Be(FileGrouping.Ungrouped);
    }
}

public class GroupedSortTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 13, 30, 0, DateTimeKind.Utc);

    private static FileSystemEntry File(string name, long size, DateTime modified)
        => new(@"C:\root\" + name, name, false, size, modified, Path.GetExtension(name));

    private static FileSystemEntry Dir(string name)
        => new(@"C:\root\" + name, name, true, 0, Now, string.Empty);

    [Fact]
    public void GroupedSort_OrdersByBucket_ThenByInnerSort()
    {
        var items = new[]
        {
            File("zeta.txt", 1, Now),
            File("alpha.txt", 1, Now),
            File("9lives.txt", 1, Now),
            File("mid.txt", 1, Now)
        };

        Array.Sort(items, FileSystemEntryComparer.ForGrouped(
            GroupByMode.Name, Now, SortColumn.Name, descending: false, DirectorySortMode.MixedWithFiles));

        items.Select(i => i.Name).Must().BeSequenceEqual(new[] { "9lives.txt", "alpha.txt", "mid.txt", "zeta.txt" });
    }

    [Fact]
    public void GroupedSort_InnerDescending_DoesNotReverseBucketOrder()
    {
        var items = new[]
        {
            File("alpha.txt", 1, Now),
            File("beta.txt", 1, Now),
            File("yak.txt", 1, Now)
        };

        Array.Sort(items, FileSystemEntryComparer.ForGrouped(
            GroupByMode.Name, Now, SortColumn.Name, descending: true, DirectorySortMode.MixedWithFiles));

        // A–H first (descending inside it), then Q–Z.
        items.Select(i => i.Name).Must().BeSequenceEqual(new[] { "beta.txt", "alpha.txt", "yak.txt" });
    }

    [Fact]
    public void GroupedSort_BySize_PutsFoldersFirst_ThenAscendingBuckets()
    {
        var items = new[]
        {
            File("huge.bin", 2L * 1024 * 1024 * 1024, Now),
            File("tiny.txt", 10, Now),
            Dir("folder"),
            File("medium.bin", 2L * 1024 * 1024, Now)
        };

        Array.Sort(items, FileSystemEntryComparer.ForGrouped(
            GroupByMode.Size, Now, SortColumn.Name, descending: false, DirectorySortMode.MixedWithFiles));

        items.Select(i => i.Name).Must().BeSequenceEqual(new[] { "folder", "tiny.txt", "medium.bin", "huge.bin" });
    }

    [Fact]
    public void GroupedSort_ByType_GroupsFoldersThenExtensionsAlphabetically()
    {
        var items = new[]
        {
            File("b.zip", 1, Now),
            File("a.md", 1, Now),
            Dir("z-folder"),
            File("c.md", 1, Now)
        };

        Array.Sort(items, FileSystemEntryComparer.ForGrouped(
            GroupByMode.Type, Now, SortColumn.Name, descending: false, DirectorySortMode.MixedWithFiles));

        items.Select(i => i.Name).Must().BeSequenceEqual(new[] { "z-folder", "a.md", "c.md", "b.zip" });
    }

    [Fact]
    public void GroupedSort_ByModified_KeepsRecentBucketsFirst_RegardlessOfInnerNameSort()
    {
        var items = new[]
        {
            File("old.txt", 1, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            File("new.txt", 1, Now),
            File("yesterday.txt", 1, Now.AddDays(-1))
        };

        Array.Sort(items, FileSystemEntryComparer.ForGrouped(
            GroupByMode.Modified, Now, SortColumn.Name, descending: false, DirectorySortMode.MixedWithFiles));

        items.Select(i => i.Name).Must().BeSequenceEqual(new[] { "new.txt", "yesterday.txt", "old.txt" });
    }

    [Fact]
    public void ForGrouped_None_MatchesUngroupedComparer()
    {
        var items = new[] { File("b.txt", 1, Now), File("a.txt", 1, Now) };
        var expected = items.ToArray();

        Array.Sort(items, FileSystemEntryComparer.ForGrouped(
            GroupByMode.None, Now, SortColumn.Name, descending: false, DirectorySortMode.MixedWithFiles));
        Array.Sort(expected, FileSystemEntryComparer.For(
            SortColumn.Name, descending: false, DirectorySortMode.MixedWithFiles));

        items.Select(i => i.Name).Must().BeSequenceEqual(expected.Select(i => i.Name).ToArray());
    }
}
