using HelixExplorer.Core.FileSystem;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class InternalClipboardServiceTests
{
    [Fact]
    public void SetCopy_RaisesChanged_AndStoresPayload()
    {
        var clipboard = new InternalClipboardService();
        var raised = 0;
        clipboard.Changed += (_, _) => raised++;

        clipboard.SetCopy(["C:\\a.txt", "C:\\b.txt"], @"C:\");

        Assert.True(clipboard.HasPayload);
        Assert.Equal(ClipboardOperation.Copy, clipboard.Current!.Operation);
        Assert.Equal(2, clipboard.Current.Paths.Count);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetCut_ThenClear_RaisesChangedTwice()
    {
        var clipboard = new InternalClipboardService();
        var raised = 0;
        clipboard.Changed += (_, _) => raised++;

        clipboard.SetCut(["C:\\a.txt"], @"C:\");
        clipboard.Clear();

        Assert.False(clipboard.HasPayload);
        Assert.Null(clipboard.Current);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Clear_WhenEmpty_DoesNotRaise()
    {
        var clipboard = new InternalClipboardService();
        var raised = 0;
        clipboard.Changed += (_, _) => raised++;

        clipboard.Clear();

        Assert.Equal(0, raised);
    }
}

public class FileOperationPathHelperTests
{
    [Fact]
    public void EnsureUniqueFilePath_ReturnsOriginalWhenMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"helix-missing-{Guid.NewGuid():N}.txt");
        Assert.Equal(path, FileOperationPathHelper.EnsureUniqueFilePath(path));
    }

    [Fact]
    public void EnsureUniqueFilePath_AppendsCounterWhenExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"helix-unique-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "file.txt");
            File.WriteAllText(path, "x");

            var unique = FileOperationPathHelper.EnsureUniqueFilePath(path);

            Assert.Equal(Path.Combine(dir, "file (1).txt"), unique);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EnsureUniqueDirectoryPath_AppendsCounterWhenExists()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"helix-dir-{Guid.NewGuid():N}");
        var path = Path.Combine(parent, "Folder");
        Directory.CreateDirectory(path);
        try
        {
            var unique = FileOperationPathHelper.EnsureUniqueDirectoryPath(path);
            Assert.Equal(Path.Combine(parent, "Folder (1)"), unique);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
