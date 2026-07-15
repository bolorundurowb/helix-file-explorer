using HelixExplorer.Core.Git;
using Xunit;

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
        Assert.True(cache.TryGet(@"C:\repo", out var got));
        Assert.Same(snap, got);
    }

    [Fact]
    public void TryGet_Misses_AfterTtlExpires()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new GitStatusCache(TimeSpan.FromMilliseconds(750), () => now);
        cache.Store(@"C:\repo", Snapshot());

        now = now.AddSeconds(2);
        Assert.False(cache.TryGet(@"C:\repo", out _));
    }

    [Fact]
    public void TryGet_IsCaseInsensitiveOnRoot()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new GitStatusCache(TimeSpan.FromSeconds(5), () => now);
        cache.Store(@"C:\Repo", Snapshot());

        Assert.True(cache.TryGet(@"c:\repo", out _));
    }

    [Fact]
    public void Invalidate_RemovesEntry()
    {
        var cache = new GitStatusCache(TimeSpan.FromSeconds(30));
        cache.Store(@"C:\repo", Snapshot());
        cache.Invalidate(@"C:\repo");
        Assert.False(cache.TryGet(@"C:\repo", out _));
    }
}
