using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public sealed class HelixVersionTests
{
    [Theory]
    [InlineData("1.4.3", 1, 4, 3, null)]
    [InlineData("0.0.0", 0, 0, 0, null)]
    [InlineData("1.4.4-preview.1", 1, 4, 4, 1)]
    [InlineData("1.4.4-preview.12", 1, 4, 4, 12)]
    [InlineData("v1.4.3", 1, 4, 3, null)]
    [InlineData("V1.4.3", 1, 4, 3, null)]
    public void TryParse_ValidVersion_ParsesComponents(
        string value, int major, int minor, int patch, int? preview)
    {
        var parsed = HelixVersion.TryParse(value, out var version);

        parsed.Must().BeTrue();
        version.Major.Must().Be(major);
        version.Minor.Must().Be(minor);
        version.Patch.Must().Be(patch);
        version.Preview.Must().Be(preview);
        version.IsStable.Must().Be(preview is null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.4")]
    [InlineData("1.4.3.1")]
    [InlineData("01.4.3")]
    [InlineData("1.04.3")]
    [InlineData("1.4.03")]
    [InlineData("1.4.3-preview")]
    [InlineData("1.4.3-preview.0")]
    [InlineData("1.4.3-preview.01")]
    [InlineData("1.4.3-alpha.1")]
    [InlineData("not-a-version")]
    public void TryParse_InvalidVersion_ReturnsFalse(string? value)
    {
        var parsed = HelixVersion.TryParse(value, out _);

        parsed.Must().BeFalse();
    }

    [Fact]
    public void Compare_HigherMajorMinorPatch_IsGreater()
    {
        HelixVersion.TryParse("2.0.0", out var higher);
        HelixVersion.TryParse("1.9.9", out var lower);

        higher.CompareTo(lower).Must().BeGreaterThan(0);
        lower.CompareTo(higher).Must().BeLessThan(0);
    }

    [Fact]
    public void Compare_StableBeatsPreviewOnSameBase()
    {
        HelixVersion.TryParse("1.4.4", out var stable);
        HelixVersion.TryParse("1.4.4-preview.9", out var preview);

        stable.CompareTo(preview).Must().BeGreaterThan(0);
        preview.CompareTo(stable).Must().BeLessThan(0);
    }

    [Fact]
    public void Compare_PreviewNumbersOrderNumerically_NotLexically()
    {
        HelixVersion.TryParse("1.4.4-preview.10", out var ten);
        HelixVersion.TryParse("1.4.4-preview.9", out var nine);

        ten.CompareTo(nine).Must().BeGreaterThan(0);
    }

    [Fact]
    public void Compare_EqualVersions_AreEqual()
    {
        HelixVersion.TryParse("1.4.3", out var left);
        HelixVersion.TryParse("1.4.3", out var right);

        left.CompareTo(right).Must().Be(0);
        left.Must().Be(right);
    }

    [Fact]
    public void Compare_VPrefixIsNormalizedBeforeComparing()
    {
        HelixVersion.TryParse("v1.4.3", out var withPrefix);
        HelixVersion.TryParse("1.4.3", out var withoutPrefix);

        withPrefix.CompareTo(withoutPrefix).Must().Be(0);
        withPrefix.Must().Be(withoutPrefix);
    }

    [Fact]
    public void Compare_HotfixBelowShippedMinor_IsNotLatest()
    {
        HelixVersion.TryParse("1.3.5", out var hotfix);
        HelixVersion.TryParse("1.4.3", out var shipped);

        hotfix.CompareTo(shipped).Must().BeLessThan(0);
    }

    [Fact]
    public void ToString_StableVersion_HasNoSuffix()
    {
        HelixVersion.TryParse("1.4.3", out var version);

        version.ToString().Must().Be("1.4.3");
    }

    [Fact]
    public void ToString_PreviewVersion_IncludesSuffix()
    {
        HelixVersion.TryParse("1.4.4-preview.2", out var version);

        version.ToString().Must().Be("1.4.4-preview.2");
    }
}
