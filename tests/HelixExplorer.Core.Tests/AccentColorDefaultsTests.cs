using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests;

public class AccentColorDefaultsTests
{
    [Fact]
    public void Resolve_UsesCustomWhenProvided()
    {
        AccentColorDefaults.Resolve(0xFF123456, isDarkTheme: false).Must().Be(0xFF123456u);
    }

    [Fact]
    public void Resolve_UsesThemeDefaultsWhenNull()
    {
        AccentColorDefaults.Resolve(null, isDarkTheme: false).Must().Be(AccentColorDefaults.Light);
        AccentColorDefaults.Resolve(null, isDarkTheme: true).Must().Be(AccentColorDefaults.Dark);
    }

    [Fact]
    public void FromHex_ParsesSixDigitHex()
    {
        AccentColorDefaults.FromHex("#0078D4").Must().Be(0xFF0078D4u);
    }
}
