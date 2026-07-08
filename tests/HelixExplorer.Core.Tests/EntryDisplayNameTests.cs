using HelixExplorer.Core.Models;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class EntryDisplayNameTests
{
    [Fact]
    public void Format_ShowsExtension_WhenEnabled()
    {
        var entry = new FileSystemEntry(@"C:\docs\report.docx", "report.docx", false, 0, DateTime.UtcNow, ".docx");

        Assert.Equal("report.docx", EntryDisplayName.Format(entry, showFileExtensions: true));
    }

    [Fact]
    public void Format_HidesExtension_WhenDisabled()
    {
        var entry = new FileSystemEntry(@"C:\docs\report.docx", "report.docx", false, 0, DateTime.UtcNow, ".docx");

        Assert.Equal("report", EntryDisplayName.Format(entry, showFileExtensions: false));
    }

    [Fact]
    public void Format_LeavesDirectoriesUnchanged()
    {
        var entry = new FileSystemEntry(@"C:\docs", "docs", true, 0, DateTime.UtcNow, string.Empty);

        Assert.Equal("docs", EntryDisplayName.Format(entry, showFileExtensions: false));
    }

    [Fact]
    public void Format_LeavesExtensionlessFilesUnchanged()
    {
        var entry = new FileSystemEntry(@"C:\docs\README", "README", false, 0, DateTime.UtcNow, string.Empty);

        Assert.Equal("README", EntryDisplayName.Format(entry, showFileExtensions: false));
    }
}
