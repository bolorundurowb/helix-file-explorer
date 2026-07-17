using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;

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

        first.Must().Be(FileConflictChoice.Skip);
        second.Must().Be(FileConflictChoice.Skip);
        resolver.ApplyToAllChosen.Must().BeTrue();
    }
}

public class ShellPathTests
{
    [Fact]
    public void IsRecycleBin_MatchesKnownPath()
    {
        ShellPath.IsRecycleBin(ShellPath.RecycleBin).Must().BeTrue();
        ShellPath.IsRecycleBin(@"C:\").Must().BeFalse();
    }
}
