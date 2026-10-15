using HelixExplorer.ViewModels;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// Exercises the pure release-selection logic behind the Stable/Preview update channel without
/// an HTTP round trip, per <see cref="SettingsPageViewModel.SelectStableRelease"/> and
/// <see cref="SettingsPageViewModel.SelectBestPreviewRelease"/>.
/// </summary>
public sealed class SettingsPageUpdateChannelTests
{
    [Fact]
    public void SelectStableRelease_ValidPayload_ReturnsVersionAndUrl()
    {
        const string json = """
            {
              "tag_name": "v1.4.3",
              "html_url": "https://github.com/bolorundurowb/helix-file-explorer/releases/tag/v1.4.3"
            }
            """;

        var selected = SettingsPageViewModel.SelectStableRelease(json);

        selected.Must().NotBeNull();
        selected!.Value.Version.ToString().Must().Be("1.4.3");
        selected.Value.Url.Must().Be("https://github.com/bolorundurowb/helix-file-explorer/releases/tag/v1.4.3");
    }

    [Fact]
    public void SelectStableRelease_UnparsableTag_ReturnsNull()
    {
        const string json = """{ "tag_name": "not-a-version", "html_url": "https://example.test" }""";

        var selected = SettingsPageViewModel.SelectStableRelease(json);

        selected.Must().BeNull();
    }

    [Fact]
    public void SelectBestPreviewRelease_PicksGreatestAcrossStableAndPreview()
    {
        const string json = """
            [
              { "draft": false, "prerelease": true, "tag_name": "v1.4.4-preview.2", "html_url": "https://example.test/preview.2" },
              { "draft": false, "prerelease": false, "tag_name": "v1.4.3", "html_url": "https://example.test/1.4.3" },
              { "draft": false, "prerelease": true, "tag_name": "v1.4.4-preview.1", "html_url": "https://example.test/preview.1" }
            ]
            """;

        var selected = SettingsPageViewModel.SelectBestPreviewRelease(json);

        selected.Must().NotBeNull();
        selected!.Value.Version.ToString().Must().Be("1.4.4-preview.2");
        selected.Value.Url.Must().Be("https://example.test/preview.2");
    }

    [Fact]
    public void SelectBestPreviewRelease_NewerStableSupersedesOlderPreview()
    {
        const string json = """
            [
              { "draft": false, "prerelease": false, "tag_name": "v1.5.0", "html_url": "https://example.test/1.5.0" },
              { "draft": false, "prerelease": true, "tag_name": "v1.4.4-preview.3", "html_url": "https://example.test/preview.3" }
            ]
            """;

        var selected = SettingsPageViewModel.SelectBestPreviewRelease(json);

        selected.Must().NotBeNull();
        selected!.Value.Version.ToString().Must().Be("1.5.0");
    }

    [Fact]
    public void SelectBestPreviewRelease_ExcludesDrafts()
    {
        const string json = """
            [
              { "draft": true, "prerelease": true, "tag_name": "v1.4.4-preview.9", "html_url": "https://example.test/draft" },
              { "draft": false, "prerelease": false, "tag_name": "v1.4.3", "html_url": "https://example.test/1.4.3" }
            ]
            """;

        var selected = SettingsPageViewModel.SelectBestPreviewRelease(json);

        selected.Must().NotBeNull();
        selected!.Value.Version.ToString().Must().Be("1.4.3");
    }

    [Fact]
    public void SelectBestPreviewRelease_SkipsUnparsableTags_ButStillPicksBest()
    {
        const string json = """
            [
              { "draft": false, "prerelease": false, "tag_name": "not-a-version", "html_url": "https://example.test/bad" },
              { "draft": false, "prerelease": false, "tag_name": "v1.4.3", "html_url": "https://example.test/1.4.3" }
            ]
            """;

        var selected = SettingsPageViewModel.SelectBestPreviewRelease(json);

        selected.Must().NotBeNull();
        selected!.Value.Version.ToString().Must().Be("1.4.3");
    }

    [Fact]
    public void SelectBestPreviewRelease_EmptyArray_ReturnsNull()
    {
        var selected = SettingsPageViewModel.SelectBestPreviewRelease("[]");

        selected.Must().BeNull();
    }
}
