using System.Globalization;
using HelixExplorer.Core.Models;

namespace HelixExplorer.ViewModels.Tests;

public sealed class EntryItemViewModelTests
{
    [Fact]
    public void HoverText_shouldShowTheDisplayedNameSizeAndLocalModifiedDate()
    {
        var modified = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        var entry = new EntryItemViewModel(new("/report.txt", "report.txt", false, 2048, modified, ".txt"),
            showFileExtensions: false);
        var local = modified.ToLocalTime().ToString("F", CultureInfo.CurrentCulture);

        var tooltip = entry.HoverText;

        tooltip.Must().Be($"report\nSize: 2.0 KiB\nDate modified: {local}");
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, -1)]
    public void HoverText_shouldOmitUnavailableMetadata(bool isDirectory, long size)
    {
        var entry = new EntryItemViewModel(new("/report", "report", isDirectory, size, DateTime.MinValue, ""));

        var tooltip = entry.HoverText;

        tooltip.Must().Be("report");
    }

    [Fact]
    public void UpdateEntry_shouldNotifyHoverText_whenMetadataChanges()
    {
        var original = new FileSystemEntry("/report.txt", "report.txt", false, 1, DateTime.MinValue, ".txt");
        var entry = new EntryItemViewModel(original);
        var notifications = new List<string?>();
        entry.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        entry.UpdateEntry(original with { SizeBytes = 2 }, true, entry.GitStatus);

        notifications.Count(name => name == nameof(EntryItemViewModel.HoverText)).Must().Be(1);
        entry.HoverText.Must().Be("report.txt\nSize: 2 B");
    }

    [Fact]
    public void SetShowFileExtensions_shouldNotifyHoverText_whenTheDisplayedNameChanges()
    {
        var entry = new EntryItemViewModel(new("/report.txt", "report.txt", false, 1, DateTime.MinValue, ".txt"));
        var notifications = new List<string?>();
        entry.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        entry.SetShowFileExtensions(false);

        notifications.Count(name => name == nameof(EntryItemViewModel.HoverText)).Must().Be(1);
        entry.HoverText.Must().Be("report\nSize: 1 B");
    }
}
