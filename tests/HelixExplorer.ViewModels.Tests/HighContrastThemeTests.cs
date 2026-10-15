using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using HelixExplorer.Core.Theming;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels.Tests;

public sealed class HighContrastThemeTests
{
    private static readonly HighContrastPalette Aquatic =
        new(0xFF202020, 0xFFFFFFFF, 0xFF8EE3F0, 0xFF263B50, 0xFFA6A6A6, 0xFF75E9FC);

    private static readonly HighContrastPalette Desert =
        new(0xFFFFFAEF, 0xFF3D3D3D, 0xFF903909, 0xFFFFF5E3, 0xFF676767, 0xFF8E2E00);

    [Fact]
    public void Contrast_theme_maps_surface_text_selection_and_accent_to_system_colours()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var source = new FakeHighContrastSource { Palette = Aquatic };
            var theme = new AvaloniaThemeService(source);
            var previous = Application.Current!.RequestedThemeVariant;
            try
            {
                theme.ApplyTheme(ThemeMode.Light);

                var requested = Application.Current.RequestedThemeVariant;
                requested!.Key.Must().Be("HighContrast");
                requested.InheritVariant.Must().Be(ThemeVariant.Dark);
                AvaloniaThemeService.EffectiveVariant.Must().Be(requested);

                BrushColor("HelixFileAreaBrush").Must().Be(Color.FromUInt32(Aquatic.Window));
                BrushColor("HelixChromeBrush").Must().Be(Color.FromUInt32(Aquatic.Window));
                BrushColor("HelixEntryForegroundBrush").Must().Be(Color.FromUInt32(Aquatic.WindowText));
                BrushColor("HelixSelectionBarBrush").Must().Be(Color.FromUInt32(Aquatic.Highlight));
                BrushColor("HelixAccentBrush").Must().Be(Color.FromUInt32(Aquatic.Highlight));
                BrushColor("HelixAccentForegroundBrush").Must().Be(Color.FromUInt32(Aquatic.HighlightText));
                BrushColor("HelixGitUntrackedBrush").Must().Be(Color.FromUInt32(Aquatic.GrayText));
                BrushColor("HelixFocusBorderBrush").Must().Be(Color.FromUInt32(Aquatic.Hotlight));
                BrushColor("HelixSelectionFillBrush").Must().Be(Colors.Transparent);
                Resource("HelixTabTintVisible").Must().Be(false);

                // Unmapped keys come from the dictionary matching the window brightness.
                BrushColor("HelixDangerBrush").Must().Be(Color.Parse("#FF99A4"));
            }
            finally
            {
                Reset(theme, source, previous);
            }
        });
    }

    [Fact]
    public void Switching_to_a_light_contrast_theme_falls_back_to_light_resources()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var source = new FakeHighContrastSource { Palette = Aquatic };
            var theme = new AvaloniaThemeService(source);
            var previous = Application.Current!.RequestedThemeVariant;
            try
            {
                theme.ApplyTheme(ThemeMode.System);
                source.Palette = Desert;
                theme.ApplyTheme(ThemeMode.System);

                Application.Current.RequestedThemeVariant!.InheritVariant.Must().Be(ThemeVariant.Light);
                BrushColor("HelixFileAreaBrush").Must().Be(Color.FromUInt32(Desert.Window));
                BrushColor("HelixDangerBrush").Must().Be(Color.Parse("#C42B1C"));
            }
            finally
            {
                Reset(theme, source, previous);
            }
        });
    }

    [Fact]
    public void Turning_contrast_off_restores_the_configured_mode()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var source = new FakeHighContrastSource { Palette = Aquatic };
            var theme = new AvaloniaThemeService(source);
            var previous = Application.Current!.RequestedThemeVariant;
            try
            {
                theme.ApplyTheme(ThemeMode.Dark);
                source.Palette = null;
                theme.ApplyTheme(ThemeMode.Dark);

                Application.Current.RequestedThemeVariant.Must().Be(ThemeVariant.Dark);
                Resource("HelixTabTintVisible").Must().Be(true);
                BrushColor("HelixFileAreaBrush").Must().Be(Color.Parse("#1E1E1E"));
            }
            finally
            {
                Reset(theme, source, previous);
            }
        });
    }

    [Fact]
    public void Custom_accent_does_not_override_contrast_colours()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var source = new FakeHighContrastSource();
            var theme = new AvaloniaThemeService(source);
            var accent = new AvaloniaAccentBrushService(new FixedAccentColorSource());
            var resources = Application.Current!.Resources;
            var previous = Application.Current.RequestedThemeVariant;
            var hadOverrides = resources.ContainsKey("HelixAccentBrush");
            try
            {
                theme.ApplyTheme(ThemeMode.Light);
                accent.ApplyCustomAccent(0xFF00FF00);
                resources.ContainsKey("HelixAccentBrush").Must().BeTrue();

                source.Palette = Aquatic;
                theme.ApplyTheme(ThemeMode.Light);
                accent.ApplyCustomAccent(0xFF00FF00);

                resources.ContainsKey("HelixAccentBrush").Must().BeFalse();
                BrushColor("HelixAccentBrush").Must().Be(Color.FromUInt32(Aquatic.Highlight));
                BrushColor("HelixSelectionFillBrush").Must().Be(Colors.Transparent);
            }
            finally
            {
                // Still in contrast mode here, so this clears the overrides the test added.
                if (!hadOverrides)
                    accent.ApplyCustomAccent(null);
                Reset(theme, source, previous);
            }
        });
    }

    private static void Reset(AvaloniaThemeService theme, FakeHighContrastSource source, ThemeVariant? previous)
    {
        source.Palette = null;
        theme.ApplyTheme(ThemeMode.System);
        Application.Current!.RequestedThemeVariant = previous;
    }

    private static object? Resource(string key)
    {
        var app = Application.Current!;
        app.TryGetResource(key, app.ActualThemeVariant, out var value).Must().BeTrue();
        return value;
    }

    private static Color BrushColor(string key)
    {
        var brush = Resource(key) as SolidColorBrush;
        brush.Must().NotBeNull();
        return brush!.Color;
    }

    private sealed class FakeHighContrastSource : IHighContrastSource
    {
        public HighContrastPalette? Palette { get; set; }

        public HighContrastPalette? TryReadActivePalette() => Palette;
    }

    private sealed class FixedAccentColorSource : IAccentColorSource
    {
        public uint? TryReadAccentColor() => AccentColorDefaults.Fallback;
    }
}
