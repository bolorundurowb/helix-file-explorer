using HelixExplorer.Core.Models;

namespace HelixExplorer.Core.Tests;

public class EntryDisplayNameTests
{
    [Fact]
    public void Format_ShowsExtension_WhenEnabled()
    {
        var entry = new FileSystemEntry(@"C:\docs\report.docx", "report.docx", false, 0, DateTime.UtcNow, ".docx");

        EntryDisplayName.Format(entry, showFileExtensions: true).Must().Be("report.docx");
    }

    [Fact]
    public void Format_HidesExtension_WhenDisabled()
    {
        var entry = new FileSystemEntry(@"C:\docs\report.docx", "report.docx", false, 0, DateTime.UtcNow, ".docx");

        EntryDisplayName.Format(entry, showFileExtensions: false).Must().Be("report");
    }

    [Fact]
    public void Format_LeavesDirectoriesUnchanged()
    {
        var entry = new FileSystemEntry(@"C:\docs", "docs", true, 0, DateTime.UtcNow, string.Empty);

        EntryDisplayName.Format(entry, showFileExtensions: false).Must().Be("docs");
    }

    [Fact]
    public void Format_LeavesExtensionlessFilesUnchanged()
    {
        var entry = new FileSystemEntry(@"C:\docs\README", "README", false, 0, DateTime.UtcNow, string.Empty);

        EntryDisplayName.Format(entry, showFileExtensions: false).Must().Be("README");
    }
}
