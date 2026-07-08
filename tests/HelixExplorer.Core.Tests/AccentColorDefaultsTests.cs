using HelixExplorer.Core.Theming;
using Xunit;

namespace HelixExplorer.Core.Tests;

public class AccentColorDefaultsTests
{
    [Fact]
    public void Resolve_UsesCustomWhenProvided()
    {
        Assert.Equal(0xFF123456u, AccentColorDefaults.Resolve(0xFF123456, isDarkTheme: false));
    }

    [Fact]
    public void Resolve_UsesThemeDefaultsWhenNull()
    {
        Assert.Equal(AccentColorDefaults.Light, AccentColorDefaults.Resolve(null, isDarkTheme: false));
        Assert.Equal(AccentColorDefaults.Dark, AccentColorDefaults.Resolve(null, isDarkTheme: true));
    }

    [Fact]
    public void FromHex_ParsesSixDigitHex()
    {
        Assert.Equal(0xFF0078D4u, AccentColorDefaults.FromHex("#0078D4"));
    }
}
