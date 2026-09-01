using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public class ClipboardOperationTagTests
{
    [Fact]
    public void Resolve_NoPublishedPayload_IsCopy()
    {
        ClipboardOperationTag.Resolve(["C:\\a.txt"], null, ClipboardOperation.Cut)
            .Must().Be(ClipboardOperation.Copy);
    }

    [Fact]
    public void Resolve_MatchingPaths_KeepsCut()
    {
        string[] paths = [@"C:\a.txt", @"C:\b.txt"];
        ClipboardOperationTag.Resolve(paths, paths, ClipboardOperation.Cut)
            .Must().Be(ClipboardOperation.Cut);
    }

    [Fact]
    public void Resolve_DifferentOsPayload_IsCopyNotCut()
    {
        ClipboardOperationTag.Resolve(
                [@"C:\explorer.txt"],
                [@"C:\helix.txt"],
                ClipboardOperation.Cut)
            .Must().Be(ClipboardOperation.Copy);
    }
}
