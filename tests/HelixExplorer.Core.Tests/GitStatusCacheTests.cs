using HelixExplorer.Core.Git;

namespace HelixExplorer.Core.Tests;

public class GitStatusCacheTests
{
    private static GitStatusSnapshot Snapshot() =>
        new(GitStatus.Empty, @"C:\repo", new Dictionary<string, GitFileStatus>());

    [Fact]
    public void TryGet_ReturnsStoredSnapshot_WithinTtl()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new GitStatusCache(TimeSpan.FromMilliseconds(750), () => now);
        var snap = Snapshot();

        cache.Store(@"C:\repo", snap);

        now = now.AddMilliseconds(500);
        cache.TryGet(@"C:\repo", out var got).Must().BeTrue();
        got.Must().Be(snap);
    }

    [Fact]
    public void TryGet_Misses_AfterTtlExpires()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new GitStatusCache(TimeSpan.FromMilliseconds(750), () => now);
        cache.Store(@"C:\repo", Snapshot());

        now = now.AddSeconds(2);
        cache.TryGet(@"C:\repo", out _).Must().BeFalse();
    }

    [Fact]
    public void TryGet_IsCaseInsensitiveOnRoot()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new GitStatusCache(TimeSpan.FromSeconds(5), () => now);
        cache.Store(@"C:\Repo", Snapshot());

        cache.TryGet(@"c:\repo", out _).Must().BeTrue();
    }

    [Fact]
    public void Invalidate_RemovesEntry()
    {
        var cache = new GitStatusCache(TimeSpan.FromSeconds(30));
        cache.Store(@"C:\repo", Snapshot());
        cache.Invalidate(@"C:\repo");
        cache.TryGet(@"C:\repo", out _).Must().BeFalse();
    }

    [Fact]
    public void Store_OverCapacity_EvictsOldestEntry()
    {
        var cache = new GitStatusCache(TimeSpan.FromMinutes(1), maxEntries: 2);
        cache.Store(@"C:\one", Snapshot());
        cache.Store(@"C:\two", Snapshot());
        cache.Store(@"C:\three", Snapshot());

        cache.Count.Must().Be(2);
        cache.TryGet(@"C:\one", out _).Must().BeFalse();
        cache.TryGet(@"C:\two", out _).Must().BeTrue();
        cache.TryGet(@"C:\three", out _).Must().BeTrue();
    }
}
