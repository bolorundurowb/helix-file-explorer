using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels.Pane;

namespace HelixExplorer.ViewModels.Tests;

public class PaneTypeAheadModelTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<EntryItemViewModel> Entries(params string[] names)
    {
        var entries = new List<EntryItemViewModel>(names.Length);
        foreach (var name in names)
        {
            var isDirectory = !name.Contains('.');
            entries.Add(new EntryItemViewModel(
                new FileSystemEntry(
                    $@"C:\folder\{name}",
                    name,
                    isDirectory,
                    isDirectory ? 0 : 10,
                    Now,
                    isDirectory ? string.Empty : Path.GetExtension(name)),
                showFileExtensions: true));
        }

        return entries;
    }

    [Fact]
    public void Resolve_SelectsFirstMatchForASingleLetter()
    {
        var entries = Entries("alpha.txt", "report.txt", "readme.md");
        var model = new PaneTypeAheadModel();

        model.Resolve("r", entries, currentIndex: -1, Now).Must().Be(1);
        model.Buffer.Must().Be("r");
    }

    [Fact]
    public void Resolve_RepeatedLetterCyclesThroughMatchesAndWraps()
    {
        var entries = Entries("report.txt", "readme.md", "zebra.txt");
        var model = new PaneTypeAheadModel();

        model.Resolve("r", entries, currentIndex: -1, Now).Must().Be(0);
        model.Resolve("r", entries, currentIndex: 0, Now).Must().Be(1);
        model.Resolve("r", entries, currentIndex: 1, Now).Must().Be(0);
    }

    [Fact]
    public void Resolve_AdditionalLettersNarrowWithinTheCurrentMatch()
    {
        var entries = Entries("readme.md", "report.txt");
        var model = new PaneTypeAheadModel();

        model.Resolve("r", entries, currentIndex: -1, Now).Must().Be(0);
        model.Resolve("e", entries, currentIndex: 0, Now).Must().Be(0);
        model.Buffer.Must().Be("re");

        model.Resolve("p", entries, currentIndex: 0, Now).Must().Be(1);
        model.Buffer.Must().Be("rep");
    }

    [Fact]
    public void Resolve_BufferExpiresAfterTheIdleTimeout()
    {
        var entries = Entries("alpha.txt", "beta.txt");
        var model = new PaneTypeAheadModel();

        model.Resolve("a", entries, currentIndex: -1, Now).Must().Be(0);

        var later = Now + PaneTypeAheadModel.BufferTimeout + TimeSpan.FromMilliseconds(1);
        model.Resolve("b", entries, currentIndex: 0, later).Must().Be(1);
        model.Buffer.Must().Be("b");
    }

    [Fact]
    public void Resolve_NoMatchLeavesTheSelectionAndKeepsTheWorkingBuffer()
    {
        var entries = Entries("readme.md", "report.txt");
        var model = new PaneTypeAheadModel();

        model.Resolve("r", entries, currentIndex: -1, Now).Must().Be(0);
        model.Resolve("q", entries, currentIndex: 0, Now).Must().Be(-1);
        model.Buffer.Must().Be("r");

        // The earlier buffer still drives the next keystroke.
        model.Resolve("e", entries, currentIndex: 0, Now).Must().Be(0);
        model.Buffer.Must().Be("re");
    }

    [Fact]
    public void Resolve_IgnoresEmptyAndControlInput()
    {
        var entries = Entries("alpha.txt");
        var model = new PaneTypeAheadModel();

        model.Resolve(null, entries, currentIndex: -1, Now).Must().Be(-1);
        model.Resolve(string.Empty, entries, currentIndex: -1, Now).Must().Be(-1);
        model.Resolve("\u0001", entries, currentIndex: -1, Now).Must().Be(-1);
        model.Buffer.Must().Be(string.Empty);
    }

    [Fact]
    public void Resolve_EmptyListingReturnsNoMatch()
    {
        var model = new PaneTypeAheadModel();

        model.Resolve("a", Array.Empty<EntryItemViewModel>(), currentIndex: -1, Now).Must().Be(-1);
    }

    [Fact]
    public void Resolve_MatchesCaseInsensitively()
    {
        var entries = Entries("Alpha.txt", "beta.txt");
        var model = new PaneTypeAheadModel();

        model.Resolve("a", entries, currentIndex: -1, Now).Must().Be(0);
    }

    [Fact]
    public void Resolve_HandlesUnusualNames()
    {
        var entries = Entries(".gitignore", " spaced.txt", "3rd-party.txt", "Ünicode.txt");
        var model = new PaneTypeAheadModel();

        model.Resolve(".", entries, currentIndex: -1, Now).Must().Be(0);

        model.Reset();
        model.Resolve(" ", entries, currentIndex: -1, Now).Must().Be(1);

        model.Reset();
        model.Resolve("3", entries, currentIndex: -1, Now).Must().Be(2);

        model.Reset();
        model.Resolve("ü", entries, currentIndex: -1, Now).Must().Be(3);
    }

    [Fact]
    public void Resolve_DuplicateDisplayNamesCycleRatherThanStick()
    {
        var entries = Entries("copy.txt", "copy.txt", "other.txt");
        var model = new PaneTypeAheadModel();

        model.Resolve("c", entries, currentIndex: -1, Now).Must().Be(0);
        model.Resolve("c", entries, currentIndex: 0, Now).Must().Be(1);
    }

    [Fact]
    public void Reset_StartsANewSearch()
    {
        var entries = Entries("readme.md", "report.txt");
        var model = new PaneTypeAheadModel();

        model.Resolve("r", entries, currentIndex: -1, Now).Must().Be(0);
        model.Reset();
        model.Buffer.Must().Be(string.Empty);

        model.Resolve("r", entries, currentIndex: 0, Now).Must().Be(1);
    }
}
