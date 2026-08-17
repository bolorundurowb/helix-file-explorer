using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public class AppPathsTests
{
    private static string ExpectedProfileFolderName =>
        AppPaths.IsDevelopmentProfile ? "HelixExplorer.Dev" : "HelixExplorer";

    [Fact]
    public void AppData_UsesProfileFolder()
    {
        Path.GetFileName(AppPaths.AppData).Must().Be(ExpectedProfileFolderName);
    }

    [Fact]
    public void AppDatabaseFile_IsHelixDbUnderAppData()
    {
        AppPaths.AppDatabaseFile.Must().Be(Path.Combine(AppPaths.AppData, "helix.db"));
    }

    [Fact]
    public void TempRoot_MatchesProfileFolder()
    {
        AppPaths.TempRoot.Must().Be(Path.Combine(Path.GetTempPath(), ExpectedProfileFolderName));
    }

    [Fact]
    public void LogsRoot_IsUnderTempDirectory()
    {
        var expectedPrefix = Path.Combine(Path.GetTempPath(), ExpectedProfileFolderName, "logs");
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
