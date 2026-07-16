using HelixExplorer.Core.Formatting;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Sorting;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class FileSizeFormatterTests
{
    [Theory]
    [InlineData(500, SizeDisplayMode.Binary, "500 B")]
    [InlineData(2048, SizeDisplayMode.Binary, "2.0 KiB")]
    [InlineData(1500, SizeDisplayMode.Decimal, "1.5 KB")]
    public void Format_UsesRequestedMode(long bytes, SizeDisplayMode mode, string expected)
    {
        Assert.Equal(expected, FileSizeFormatter.Format(bytes, mode, isDirectory: false));
    }

    [Fact]
    public void Format_Directory_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, FileSizeFormatter.Format(1024, SizeDisplayMode.Binary, isDirectory: true));
    }
}

public class RelativeTimeFormatterTests
{
    [Fact]
    public void Format_JustNow()
    {
        var now = DateTime.UtcNow;
        Assert.Equal("few seconds ago", RelativeTimeFormatter.Format(now.AddSeconds(-10), now));
    }

    [Fact]
    public void Format_HoursAgo()
    {
        var now = DateTime.UtcNow;
        Assert.Equal("2 hours ago", RelativeTimeFormatter.Format(now.AddHours(-2), now));
    }
}

public class FileSystemEntryComparerTests
{
    [Fact]
    public void Directories_AlwaysPrecedeFiles()
    {
        var file = new FileSystemEntry(@"C:\a.txt", "a.txt", false, 10, DateTime.UtcNow, ".txt");
        var dir = new FileSystemEntry(@"C:\b", "b", true, 0, DateTime.UtcNow, "");
        var items = new[] { file, dir };
        Array.Sort(items, FileSystemEntryComparer.For(SortColumn.Name, descending: false));
        Assert.True(items[0].IsDirectory);
        Assert.False(items[1].IsDirectory);
    }

    [Fact]
    public void SizeDescending_SortsLargestFirst_AmongFiles()
    {
        var small = new FileSystemEntry(@"C:\a", "a", false, 10, DateTime.UtcNow, ".txt");
        var large = new FileSystemEntry(@"C:\b", "b", false, 100, DateTime.UtcNow, ".txt");
        var items = new[] { small, large };
        Array.Sort(items, FileSystemEntryComparer.For(SortColumn.Size, descending: true));
        Assert.Equal(100, items[0].SizeBytes);
    }

    [Fact]
    public void ModifiedDescending_KeepsDirectoriesBeforeFiles()
    {
        var oldDir = new FileSystemEntry(@"C:\old", "old", true, 0, DateTime.UtcNow.AddDays(-30), "");
        var newFile = new FileSystemEntry(@"C:\new.txt", "new.txt", false, 10, DateTime.UtcNow, ".txt");
        var items = new[] { newFile, oldDir };
        Array.Sort(items, FileSystemEntryComparer.For(SortColumn.Modified, descending: true));
        Assert.True(items[0].IsDirectory);
        Assert.False(items[1].IsDirectory);
    }

    [Fact]
    public void FoldersFirst_IsDefaultDirectorySortMode()
    {
        var file = new FileSystemEntry(@"C:\a.txt", "a.txt", false, 10, DateTime.UtcNow, ".txt");
        var dir = new FileSystemEntry(@"C:\z", "z", true, 0, DateTime.UtcNow, "");
        var items = new[] { file, dir };
        Array.Sort(items, FileSystemEntryComparer.For(SortColumn.Name, descending: false));
        Assert.True(items[0].IsDirectory);
    }
}

public class DirectorySortModeTests
{
    private static readonly FileSystemEntry Apple = new(@"C:\apple.txt", "apple.txt", false, 300, new DateTime(2026, 1, 3), ".txt");
    private static readonly FileSystemEntry Banana = new(@"C:\banana", "banana", true, 0, new DateTime(2026, 1, 1), "");
    private static readonly FileSystemEntry Cherry = new(@"C:\cherry.md", "cherry.md", false, 100, new DateTime(2026, 1, 2), ".md");
    private static readonly FileSystemEntry Dates = new(@"C:\dates", "dates", true, 0, new DateTime(2026, 1, 4), "");

    private static FileSystemEntry[] SortMixed(SortColumn column, bool descending)
    {
        var items = new[] { Apple, Banana, Cherry, Dates };
        Array.Sort(items, FileSystemEntryComparer.For(column, descending, DirectorySortMode.MixedWithFiles));
        return items;
    }

    [Fact]
    public void MixedName_Ascending_InterleavesFoldersAndFiles()
    {
        var items = SortMixed(SortColumn.Name, descending: false);
        Assert.Equal(new[] { "apple.txt", "banana", "cherry.md", "dates" }, items.Select(i => i.Name));
    }

    [Fact]
    public void MixedName_Descending_InterleavesFoldersAndFiles()
    {
        var items = SortMixed(SortColumn.Name, descending: true);
        Assert.Equal(new[] { "dates", "cherry.md", "banana", "apple.txt" }, items.Select(i => i.Name));
    }

    [Fact]
    public void FoldersFirst_Name_GroupsDirectories()
    {
        var items = new[] { Apple, Banana, Cherry, Dates };
        Array.Sort(items, FileSystemEntryComparer.For(SortColumn.Name, descending: false, DirectorySortMode.FoldersFirst));
        Assert.Equal(new[] { "banana", "dates", "apple.txt", "cherry.md" }, items.Select(i => i.Name));
    }

    [Fact]
    public void FilesFirst_Name_GroupsFilesBeforeDirectories()
    {
        var items = new[] { Apple, Banana, Cherry, Dates };
        Array.Sort(items, FileSystemEntryComparer.For(SortColumn.Name, descending: false, DirectorySortMode.FilesFirst));
        Assert.Equal(new[] { "apple.txt", "cherry.md", "banana", "dates" }, items.Select(i => i.Name));
    }

    [Fact]
    public void FilesFirst_ModifiedDescending_GroupsFilesThenSortsWithinGroups()
    {
        var items = new[] { Apple, Banana, Cherry, Dates };
        Array.Sort(items, FileSystemEntryComparer.For(SortColumn.Modified, descending: true, DirectorySortMode.FilesFirst));
        Assert.Equal(new[] { "apple.txt", "cherry.md", "dates", "banana" }, items.Select(i => i.Name));
    }

    [Fact]
    public void MixedSize_Descending_OrdersBySizeIgnoringKind()
    {
        var items = SortMixed(SortColumn.Size, descending: true);
        // Directories report SizeBytes=0, so they sort after files when size-descending.
        Assert.Equal("apple.txt", items[0].Name);
        Assert.Equal("cherry.md", items[1].Name);
        Assert.Equal(new[] { "banana", "dates" }, items.Skip(2).Select(i => i.Name));
    }

    [Fact]
    public void MixedModified_Ascending_OrdersByDateIgnoringKind()
    {
        var items = SortMixed(SortColumn.Modified, descending: false);
        Assert.Equal(new[] { "banana", "cherry.md", "apple.txt", "dates" }, items.Select(i => i.Name));
    }

    [Fact]
    public void MixedType_Ascending_OrdersByExtensionIgnoringKind()
    {
        var items = SortMixed(SortColumn.Type, descending: false);
        // Type sort keys off Extension; directories use "" so they group before .md/.txt.
        Assert.Equal(new[] { "banana", "dates", "cherry.md", "apple.txt" }, items.Select(i => i.Name));
    }
}
