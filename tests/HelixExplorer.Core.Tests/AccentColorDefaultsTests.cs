using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests;

public class AccentColorDefaultsTests
{
    [Fact]
    public void Resolve_UsesCustomWhenProvided()
    {
        AccentColorDefaults.Resolve(0xFF123456, 0xFF0078D4).Must().Be(0xFF123456u);
    }

    [Fact]
    public void Resolve_UsesSystemAccentWhenCustomIsNull()
    {
        AccentColorDefaults.Resolve(null, 0xFF107C10).Must().Be(0xFF107C10u);
    }

    [Fact]
    public void Resolve_FallsBackToWindowsBlueWhenBothMissing()
    {
        AccentColorDefaults.Resolve(null, null).Must().Be(AccentColorDefaults.Fallback);
        AccentColorDefaults.ContrastRatio(AccentColorDefaults.Fallback, 0xFFFFFFFFu).Must().BeGreaterThan(3);
    }

    [Fact]
    public void FromHex_ParsesSixDigitHex()
    {
        AccentColorDefaults.FromHex("#0078D4").Must().Be(0xFF0078D4u);
    }

    [Fact]
    public void FromAbgr_ConvertsWindowsDwmAccent()
    {
        AccentColorDefaults.FromAbgr(0xFFD47800).Must().Be(0xFF0078D4u);
    }

    [Fact]
    public void FromAbgr_TreatsZeroAlphaAsOpaque()
    {
        AccentColorDefaults.FromAbgr(0x00D47800).Must().Be(0xFF0078D4u);
    }

    [Fact]
    public void FromAbgr_TreatsZeroAsMissing()
    {
        AccentColorDefaults.FromAbgr(0).Must().BeNull();
    }

    [Fact]
    public void ForegroundFor_PicksHigherContrast()
    {
        AccentColorDefaults.ForegroundFor(0xFF0078D4).Must().Be(0xFF000000u);
        AccentColorDefaults.ForegroundFor(0xFF005A9E).Must().Be(0xFFFFFFFFu);
        AccentColorDefaults.ForegroundFor(0xFFFFFFFF).Must().Be(0xFF000000u);
        AccentColorDefaults.ForegroundFor(0xFF000000).Must().Be(0xFFFFFFFFu);
    }

    [Fact]
    public void LightHover_CompositesNoDarkerThanF5()
    {
        var hover = AccentColorDefaults.CompositeOver(255, 255, 255, 0, 0, 0, AccentColorDefaults.LightHoverAlpha);
        hover.R.Must().BeGreaterThanOrEqualTo((byte)0xF5);
        hover.G.Must().Be(hover.R);
        hover.B.Must().Be(hover.R);
    }

    [Fact]
    public void SelectionTint_WindowsBlueIsDarkerThanHoverOnWhite()
    {
        var tint = AccentColorDefaults.SelectionTintFor(0xFF0078D4, isDark: false);
        tint.Neutral.Must().BeFalse();

        var hover = AccentColorDefaults.CompositeOver(255, 255, 255, 0, 0, 0, AccentColorDefaults.LightHoverAlpha);
        var fill = AccentColorDefaults.CompositeOver(255, 255, 255, 0x00, 0x78, 0xD4, tint.Alpha);
        var hoverL = AccentColorDefaults.RelativeLuminance(hover.R, hover.G, hover.B);
        var fillL = AccentColorDefaults.RelativeLuminance(fill.R, fill.G, fill.B);
        fillL.Must().BeLessThan(hoverL);
    }

    [Fact]
    public void SelectionTint_PaleAccentUsesNeutralDarkening()
    {
        var tint = AccentColorDefaults.SelectionTintFor(0xFFFFFFFF, isDark: false);
        tint.Neutral.Must().BeTrue();
        tint.Alpha.Must().Be(AccentColorDefaults.LightNeutralSelectionAlpha);
    }

    [Fact]
    public void SelectionTint_DarkWindowsBlueSeparatesMoreThanHover()
    {
        var tint = AccentColorDefaults.SelectionTintFor(0xFF0078D4, isDark: true);
        tint.Neutral.Must().BeFalse();

        var backgroundL = AccentColorDefaults.RelativeLuminance(0x1E, 0x1E, 0x1E);
        var hover = AccentColorDefaults.CompositeOver(0x1E, 0x1E, 0x1E, 255, 255, 255, AccentColorDefaults.DarkHoverAlpha);
        var fill = AccentColorDefaults.CompositeOver(0x1E, 0x1E, 0x1E, 0x00, 0x78, 0xD4, tint.Alpha);
        var hoverDistance = Math.Abs(AccentColorDefaults.RelativeLuminance(hover.R, hover.G, hover.B) - backgroundL);
        var fillDistance = Math.Abs(AccentColorDefaults.RelativeLuminance(fill.R, fill.G, fill.B) - backgroundL);
        fillDistance.Must().BeGreaterThan(hoverDistance);
    }
}
