using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("", "unknown")]
    [InlineData("   ", "unknown")]
    [InlineData(null, "unknown")]
    public void SanitizeForPath_EmptyOrWhitespace_ReturnsUnknown(string? version, string expected)
    {
        AppVersion.SanitizeForPath(version!).Must().Be(expected);
    }

    [Fact]
    public void SanitizeForPath_SimpleVersion_IsUnchanged()
    {
        AppVersion.SanitizeForPath("1.2.1").Must().Be("1.2.1");
    }

    [Fact]
    public void SanitizeForPath_TrimsSurroundingWhitespace()
    {
        AppVersion.SanitizeForPath("  1.2.1  ").Must().Be("1.2.1");
    }

    [Fact]
    public void SanitizeForPath_ReplacesForwardSlashWithUnderscore()
    {
        AppVersion.SanitizeForPath("1.2.1/beta").Must().Be("1.2.1_beta");
    }

    [Fact]
    public void SanitizeForPath_ReplacesBackslashWithUnderscore()
    {
        AppVersion.SanitizeForPath(@"1.2.1\beta").Must().Be("1.2.1_beta");
    }

    [Fact]
    public void SanitizeForPath_ReplacesInvalidFileNameCharacters()
    {
        AppVersion.SanitizeForPath("1.2.1:beta").Must().Be("1.2.1_beta");
    }

    [Fact]
    public void Current_IsNotEmpty()
    {
        string.IsNullOrWhiteSpace(AppVersion.Current).Must().BeFalse();
    }

    [Fact]
    public void CurrentForPath_EqualsSanitizedCurrent()
    {
        AppVersion.CurrentForPath.Must().Be(AppVersion.SanitizeForPath(AppVersion.Current));
    }

    [Fact]
    public void CurrentForPath_ContainsNoPathSeparators()
    {
        AppVersion.CurrentForPath.Must().NotContain("/");
        AppVersion.CurrentForPath.Must().NotContain(@"\");
    }

    [Theory]
    [InlineData("1.4.4-preview.3", true)]
    [InlineData("1.4.4-preview.1", true)]
    [InlineData("1.4.4", false)]
    [InlineData("0.0.0", false)]
    [InlineData("not-a-version", false)]
    [InlineData("", false)]
    public void IsPreviewVersion_TrueOnlyForPreviewSuffix(string version, bool expected)
    {
        AppVersion.IsPreviewVersion(version).Must().Be(expected);
    }
}
