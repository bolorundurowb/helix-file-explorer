using HelixExplorer.Core.Filtering;
using HelixExplorer.Core.Models;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class FilterPerformanceTests
{
    private static FileSystemEntry Entry(int index)
        => new($@"C:\folder\file-{index:D5}.txt", $"file-{index:D5}.txt", false, index, DateTime.UtcNow, ".txt");

    [Fact]
    public void Apply_10kEntries_CompletesQuickly()
    {
        var source = new List<FileSystemEntry>(10_000);
        for (var i = 0; i < 10_000; i++)
            source.Add(Entry(i));

        var dest = new List<FileSystemEntry>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var count = FileNameFilter.Apply(source, "123", dest);
        sw.Stop();

        Assert.True(count > 0);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Filter took {sw.ElapsedMilliseconds}ms");
    }
}
