using HelixExplorer.Core.Logging;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Tests;

public class RollingFileLoggerProviderTests
{
    [Fact]
    public void Write_CreatesVersionedLogFileWithHeader()
    {
        var directory = CreateTempDirectory();
        try
        {
            using var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions
            {
                Version = "0.2.1",
                LogsDirectory = directory,
                MinLevel = LogLevel.Information,
            });

            var logger = provider.CreateLogger("Test.Category");
            logger.LogInformation("hello from test");

            var files = Directory.GetFiles(directory, "helix-explorer-*.log");
            files.Must().HaveCount(1);

            var content = File.ReadAllText(files[0]);
            content.Must().Contain("# Helix Explorer log — version 0.2.1");
            content.Must().Contain("hello from test");
            content.Must().Contain("Test.Category");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public void Write_RollsWhenMaxSizeExceeded()
    {
        var directory = CreateTempDirectory();
        try
        {
            using var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions
            {
                Version = "0.2.1",
                LogsDirectory = directory,
                MinLevel = LogLevel.Information,
                MaxFileSizeBytes = 1024,
                RetainedFileCount = 10,
            });

            var logger = provider.CreateLogger("Roll");
            for (var i = 0; i < 40; i++)
                logger.LogInformation(new string('x', 80));

            var files = Directory.GetFiles(directory, "helix-explorer-*.log");
            files.Length.Must().BeGreaterThan(1);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public void Write_RespectsMinimumLevel()
    {
        var directory = CreateTempDirectory();
        try
        {
            using var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions
            {
                Version = "0.2.1",
                LogsDirectory = directory,
                MinLevel = LogLevel.Warning,
            });

            var logger = provider.CreateLogger("Level");
            logger.LogInformation("should not appear");
            logger.LogWarning("should appear");

            var content = File.ReadAllText(Directory.GetFiles(directory, "helix-explorer-*.log").Single());
            content.Must().NotContain("should not appear");
            content.Must().Contain("should appear");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string CreateTempDirectory()
        => Directory.CreateTempSubdirectory("helix-logger-tests-").FullName;

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for CI temp files.
        }
    }
}
