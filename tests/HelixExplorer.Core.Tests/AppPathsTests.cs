using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void LogsRoot_IsUnderTempDirectory()
    {
        var expectedPrefix = Path.Combine(Path.GetTempPath(), "HelixExplorer", "logs");
        AppPaths.LogsRoot.Must().Be(expectedPrefix);
    }

    [Fact]
    public void GetVersionedLogsDirectory_IncludesVersionSegment()
    {
        var directory = AppPaths.GetVersionedLogsDirectory("0.2.1");
        directory.Must().Be(Path.Combine(AppPaths.LogsRoot, "0.2.1"));
    }

    [Fact]
    public void GetVersionedLogsDirectory_SanitizesInvalidCharacters()
    {
        var directory = AppPaths.GetVersionedLogsDirectory("1.0.0/beta");
        directory.Must().Be(Path.Combine(AppPaths.LogsRoot, "1.0.0_beta"));
    }
}
