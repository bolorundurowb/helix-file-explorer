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
}
