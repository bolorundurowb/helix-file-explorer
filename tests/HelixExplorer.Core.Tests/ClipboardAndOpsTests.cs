using HelixExplorer.Core.FileSystem;

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

        clipboard.HasPayload.Must().BeTrue();
        clipboard.Current!.Operation.Must().Be(ClipboardOperation.Copy);
        clipboard.Current.Paths.Count.Must().Be(2);
        raised.Must().Be(1);
    }

    [Fact]
    public void SetCut_ThenClear_RaisesChangedTwice()
    {
        var clipboard = new InternalClipboardService();
        var raised = 0;
        clipboard.Changed += (_, _) => raised++;

        clipboard.SetCut(["C:\\a.txt"], @"C:\");
        clipboard.Clear();

        clipboard.HasPayload.Must().BeFalse();
        clipboard.Current.Must().BeNull();
        raised.Must().Be(2);
    }

    [Fact]
    public void Clear_WhenEmpty_DoesNotRaise()
    {
        var clipboard = new InternalClipboardService();
        var raised = 0;
        clipboard.Changed += (_, _) => raised++;

        clipboard.Clear();

        raised.Must().Be(0);
    }
}

public class FileOperationPathHelperTests
{
    [Fact]
    public void EnsureUniqueFilePath_ReturnsOriginalWhenMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"helix-missing-{Guid.NewGuid():N}.txt");
        FileOperationPathHelper.EnsureUniqueFilePath(path).Must().Be(path);
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

            unique.Must().Be(Path.Combine(dir, "file (1).txt"));
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
            unique.Must().Be(Path.Combine(parent, "Folder (1)"));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
