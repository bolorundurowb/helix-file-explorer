using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public sealed class DropEffectPolicyTests
{
    [Theory]
    [InlineData(@"C:\a", @"C:\b", false, false, false, DragDropEffects.Move)]
    [InlineData(@"C:\a", @"D:\b", false, false, false, DragDropEffects.Copy)]
    [InlineData(@"C:\a", @"\\server\share\b", false, false, false, DragDropEffects.Copy)]
    [InlineData(@"\\server\share\a", @"\\server\share\b", false, false, false, DragDropEffects.Move)]
    [InlineData(@"\\server\share\a", @"\\other\share\b", false, false, false, DragDropEffects.Copy)]
    [InlineData(@"C:\a", @"C:\b", true, false, false, DragDropEffects.Copy)]
    [InlineData(@"C:\a", @"D:\b", false, true, false, DragDropEffects.Move)]
    [InlineData(@"C:\a", @"C:\b", false, false, true, DragDropEffects.Link)]
    [InlineData(@"C:\a", @"D:\b", true, false, true, DragDropEffects.Link)]
    [InlineData(@"C:\a", @"D:\b", true, true, false, DragDropEffects.Copy)]
    [InlineData("", @"C:\b", false, false, false, DragDropEffects.Copy)]
    public void Resolve_UsesVolumeThenModifiers(
        string source,
        string destination,
        bool ctrl,
        bool shift,
        bool alt,
        DragDropEffects expected)
    {
        DropEffectPolicy.Resolve(source, destination, ctrl, shift, alt).Must().Be(expected);
    }

    [Fact]
    public void Resolve_NullPaths_CopyUnlessAModifierForcesOtherwise()
    {
        DropEffectPolicy.Resolve(null, null, false, false, false).Must().Be(DragDropEffects.Copy);
        DropEffectPolicy.Resolve(null, @"C:\b", false, true, false).Must().Be(DragDropEffects.Move);
        DropEffectPolicy.Resolve(@"C:\a", null, false, false, true).Must().Be(DragDropEffects.Link);
    }
}
