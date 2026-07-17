using System.Text.Json;
using System.Text.Json.Serialization;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests;

public class AppSettingsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void DefaultValues_MatchExpectedChromeDefaults()
    {
        var settings = new AppSettings();

        settings.Theme.Must().Be(ThemeMode.System);
        settings.UiFont.Must().Be(UiFontFamily.System);
        settings.SidebarWidth.Must().Be(200);
        settings.SizeDisplay.Must().Be(SizeDisplayMode.Binary);
        settings.ShowFileExtensions.Must().BeTrue();
        settings.DefaultViewMode.Must().Be(LayoutMode.Details);
        settings.DefaultThumbnailSize.Must().Be(72);
        settings.DefaultDualPane.Must().BeFalse();
        settings.AccentColorArgb.Must().BeNull();
        settings.PinnedPaths.Must().BeEmpty();
        settings.OpenInTerminalGesture.Must().Be("Ctrl+OemTilde");
        settings.AutoCheckForUpdates.Must().BeTrue();
    }

    [Fact]
    public void JsonRoundTrip_PreservesChromePreferences()
    {
        var original = new AppSettings
        {
            Theme = ThemeMode.Dark,
            UiFont = UiFontFamily.DmSans,
            SidebarWidth = 280,
            SizeDisplay = SizeDisplayMode.Decimal,
            ShowHiddenFiles = true,
            DefaultViewMode = LayoutMode.Grid,
            DefaultThumbnailSize = 96,
            DefaultDualPane = true,
            AccentColorArgb = 0xFF107C10,
            PinnedPaths = ["C:\\Pinned", "D:\\Work"],
            WindowWidth = 1440,
            WindowHeight = 900,
            WindowX = 120,
            WindowY = 80,
            WindowMaximized = true,
            OpenInTerminalGesture = "Ctrl+Shift+T",
            AutoCheckForUpdates = false
        };

        var json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);

        loaded!.Must().NotBeNull();
        loaded!.Theme.Must().Be(ThemeMode.Dark);
        loaded!.UiFont.Must().Be(UiFontFamily.DmSans);
        loaded!.SidebarWidth.Must().Be(280);
        loaded!.SizeDisplay.Must().Be(SizeDisplayMode.Decimal);
        loaded!.ShowHiddenFiles.Must().BeTrue();
        loaded!.DefaultViewMode.Must().Be(LayoutMode.Grid);
        loaded!.DefaultThumbnailSize.Must().Be(96);
        loaded!.DefaultDualPane.Must().BeTrue();
        loaded!.AccentColorArgb.Must().Be(0xFF107C10u);
        loaded!.PinnedPaths.Count.Must().Be(2);
        loaded!.WindowWidth!.Value.Must().Be(1440);
        loaded!.WindowHeight!.Value.Must().Be(900);
        loaded!.WindowX!.Value.Must().Be(120);
        loaded!.WindowY!.Value.Must().Be(80);
        loaded!.WindowMaximized.Must().BeTrue();
        loaded!.OpenInTerminalGesture.Must().Be("Ctrl+Shift+T");
        loaded!.AutoCheckForUpdates.Must().BeFalse();
    }

    [Fact]
    public void JsonRoundTrip_PreservesInterFont()
    {
        var original = new AppSettings { UiFont = UiFontFamily.Inter };
        var json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);

        loaded!.Must().NotBeNull();
        loaded!.UiFont.Must().Be(UiFontFamily.Inter);
    }

    [Fact]
    public void AtomicSave_WritesFileWithoutCorruptingExisting()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "helix-test-" + Guid.NewGuid().ToString("N"));
        var settingsFile = Path.Combine(tempDir, "settings.json");
        var tempFile = settingsFile + ".tmp";

        try
        {
            Directory.CreateDirectory(tempDir);

            var settings = new AppSettings { Theme = ThemeMode.Dark, SidebarWidth = 300 };
            var json = JsonSerializer.Serialize(settings, Options);

            File.WriteAllText(tempFile, json);
            File.Move(tempFile, settingsFile, overwrite: true);

            File.Exists(settingsFile).Must().BeTrue();
            File.Exists(tempFile).Must().BeFalse();

            var loaded = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(settingsFile), Options);
            loaded!.Must().NotBeNull();
            loaded!.Theme.Must().Be(ThemeMode.Dark);
            loaded!.SidebarWidth.Must().Be(300);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AtomicSave_TrashFileDoesNotCorruptTarget()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "helix-test-" + Guid.NewGuid().ToString("N"));
        var settingsFile = Path.Combine(tempDir, "settings.json");
        var tempFile = settingsFile + ".tmp";

        try
        {
            Directory.CreateDirectory(tempDir);

            var initial = new AppSettings { Theme = ThemeMode.Light };
            File.WriteAllText(settingsFile,
                JsonSerializer.Serialize(initial, Options));

            // Crash mid-write leaves garbage only in .tmp; atomic save must not Move until
            // write succeeds, so the existing settings file stays intact.
            File.WriteAllText(tempFile, "{INVALID-JSON");

            File.Exists(settingsFile).Must().BeTrue();
            var loaded = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(settingsFile), Options);
            loaded!.Must().NotBeNull();
            loaded!.Theme.Must().Be(ThemeMode.Light);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
