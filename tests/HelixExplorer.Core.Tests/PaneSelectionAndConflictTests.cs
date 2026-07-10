using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class FileConflictResolverTests
{
    private sealed class FakeDialogs : IUserDialogService
    {
        public FileConflictResolution? Next { get; set; }

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;

        public Task ShowOperationSummaryAsync(FileOperationResult result, string operationName) => Task.CompletedTask;

        public Task<FileConflictResolution?> ResolveConflictAsync(FileConflictInfo conflict, bool canApplyToAll)
            => Task.FromResult(Next);
    }

    [Fact]
    public async Task ResolveAsync_AppliesApplyToAllChoice()
    {
        var dialogs = new FakeDialogs
        {
            Next = new FileConflictResolution(FileConflictChoice.Skip, ApplyToAll: true)
        };
        var resolver = new FileConflictResolver(dialogs);

        var first = await resolver.ResolveAsync(new FileConflictInfo("a", "b", false));
        var second = await resolver.ResolveAsync(new FileConflictInfo("c", "d", false));

        Assert.Equal(FileConflictChoice.Skip, first);
        Assert.Equal(FileConflictChoice.Skip, second);
        Assert.True(resolver.ApplyToAllChosen);
    }
}

public class ShellPathTests
{
    [Fact]
    public void IsRecycleBin_MatchesKnownPath()
    {
        Assert.True(ShellPath.IsRecycleBin(ShellPath.RecycleBin));
        Assert.False(ShellPath.IsRecycleBin(@"C:\"));
    }
}
