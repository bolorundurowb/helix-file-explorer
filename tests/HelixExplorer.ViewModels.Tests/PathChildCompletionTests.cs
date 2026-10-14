using Avalonia.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Views;

namespace HelixExplorer.ViewModels.Tests;

public class PathChildCompletionTests
{
    [Theory]
    [InlineData(@"C:\Users\me", @"C:\Users", "me")]
    [InlineData(@"C:\Users\me\", @"C:\Users\me", "")]
    [InlineData(@"C:\Users", @"C:\", "Users")]
    [InlineData(@"C:\", @"C:\", "")]
    [InlineData(@"C:", @"C:\", "")]
    [InlineData(@"C:/Users/me", @"C:\Users", "me")]
    [InlineData(@"\\server\share\docs", @"\\server\share", "docs")]
    [InlineData(@"\\server\share\", @"\\server\share", "")]
    [InlineData(@"\\server\share", @"\\server", "share")]
    [InlineData(@"\\server", @"\\server", "")]
    [InlineData(@"\\server\", @"\\server", "")]
    [InlineData(@"//server/share/docs", @"\\server\share", "docs")]
    public void TryParse_SplitsParentAndPrefix(string text, string directory, string prefix)
    {
        PathChildCompletion.TryParse(text, out var query).Must().BeTrue();
        query.Directory.Must().Be(directory);
        query.Prefix.Must().Be(prefix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\")]
    [InlineData("Users\\me")]
    [InlineData("shell:RecycleBinFolder")]
    [InlineData("archive://C%3A%5Cfoo.zip!/inner")]
    [InlineData("__home__")]
    public void TryParse_RejectsPathsThatAreNotOneFilesystemLevel(string? text)
    {
        PathChildCompletion.TryParse(text, out _).Must().BeFalse();
    }

    [Fact]
    public void Complete_AppendsTheChildAndASeparator()
    {
        var parent = new PathChildCompletion.Query(@"C:\Users", "me");
        PathChildCompletion.Complete(parent, "me").Must().Be(@"C:\Users\me\");

        var root = new PathChildCompletion.Query(@"C:\", "Win");
        PathChildCompletion.Complete(root, "Windows").Must().Be(@"C:\Windows\");

        var share = new PathChildCompletion.Query(@"\\server\share", "docs");
        PathChildCompletion.Complete(share, "docs").Must().Be(@"\\server\share\docs\");
    }

    [Fact]
    public void NameMatches_IsACaseInsensitivePrefix()
    {
        PathChildCompletion.NameMatches("Documents", "").Must().BeTrue();
        PathChildCompletion.NameMatches("Documents", "doc").Must().BeTrue();
        PathChildCompletion.NameMatches("Documents", "Documents").Must().BeTrue();
        PathChildCompletion.NameMatches("Documents", "Desktop").Must().BeFalse();
    }

    [Fact]
    public void TakeMatches_SortsTheWindowAndStopsAtTheLimit()
    {
        var names = new[] { "zeta", "Alpha", "alpine", "beta", "other" };
        var matches = PathChildCompletion.TakeMatches(names, "a", limit: 2);
        matches.Must().HaveCount(2);
        matches[0].Must().Be("Alpha");
        matches[1].Must().Be("alpine");
    }

    [Theory]
    [InlineData(Key.Tab, KeyModifiers.None, true)]
    [InlineData(Key.Down, KeyModifiers.None, true)]
    [InlineData(Key.Tab, KeyModifiers.Shift, false)]
    [InlineData(Key.Down, KeyModifiers.Control, false)]
    [InlineData(Key.Enter, KeyModifiers.None, false)]
    [InlineData(Key.Up, KeyModifiers.None, false)]
    public void ShouldAcceptSuggestion_IsPlainTabOrDown(Key key, KeyModifiers modifiers, bool accepts)
    {
        PathChildCompletion.ShouldAcceptSuggestion(key, modifiers).Must().Be(accepts);
    }

    [Fact]
    public void ShellAndArchivePaths_AreNotChildMenus()
    {
        ShellPath.IsShellPath(ShellPath.RecycleBin).Must().BeTrue();
        PathChildCompletion.TryParse(ShellPath.RecycleBin, out _).Must().BeFalse();
    }
}
