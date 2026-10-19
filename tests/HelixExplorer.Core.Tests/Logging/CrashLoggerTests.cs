using System.Globalization;
using HelixExplorer.Core.Logging;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Tests.Logging;

// InstallEarly is not called: it subscribes process-wide handlers that would stay attached
// for the rest of the test run. The handler bodies are covered on their own.
public sealed class CrashLoggerTests : IDisposable
{
    private readonly string _directory;

    public CrashLoggerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "helix-crash-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        CrashLogger.SetLogsDirectory(_directory);
    }

    [Fact]
    public void Report_shouldLogCriticalAndWriteTheCrashFile()
    {
        var logger = AttachLogger();
        var exception = new InvalidOperationException("disk failed");

        CrashLogger.Report("Main", exception);

        logger.Entries.Must().HaveCount(1);
        logger.Entries[0].Level.Must().Be(LogLevel.Critical);
        logger.Entries[0].Exception.Must().Be(exception);
        logger.Entries[0].Message.Must().Be("Main");
        var content = ReadCrashFile();
        content.Must().Contain("[Crash] Main");
        content.Must().Contain(nameof(InvalidOperationException));
        content.Must().Contain("disk failed");
    }

    [Fact]
    public void Report_shouldWriteTheCrashFile_whenNoLoggerIsAttached()
    {
        CrashLogger.Report("Main", new InvalidOperationException("before host"));

        ReadCrashFile().Must().Contain("before host");
    }

    [Fact]
    public void Report_shouldLogError_whenTheReportIsNotFatal()
    {
        var logger = AttachLogger();
        var exception = new InvalidOperationException("ui thread");

        CrashLogger.Report("Dispatcher.UIThread.UnhandledException", exception, fatal: false);

        logger.Entries.Must().HaveCount(1);
        logger.Entries[0].Level.Must().Be(LogLevel.Error);
        logger.Entries[0].Exception.Must().Be(exception);
        logger.Entries[0].Message.Must().Be("Dispatcher.UIThread.UnhandledException");
    }

    [Fact]
    public void Report_shouldIncludeTheDetail_whenDetailIsProvided()
    {
        var logger = AttachLogger();

        CrashLogger.Report("Main", new InvalidOperationException("disk failed"), "IsTerminating=True");

        logger.Entries[0].Message.Must().Be("Main (IsTerminating=True)");
        ReadCrashFile().Must().Contain("[Crash] Main (IsTerminating=True)");
    }

    [Fact]
    public void Report_shouldRecordThatNoExceptionWasSupplied_whenExceptionIsNull()
    {
        var logger = AttachLogger();

        CrashLogger.Report("Main", null);

        logger.Entries.Must().HaveCount(1);
        logger.Entries[0].Exception.Must().BeNull();
        ReadCrashFile().Must().Contain("(no exception object)");
    }

    [Fact]
    public void Report_shouldAppendToTheSameCrashFile_whenCalledAgain()
    {
        CrashLogger.Report("Main", new InvalidOperationException("first failure"));

        CrashLogger.Report("Main", new InvalidOperationException("second failure"));

        var content = ReadCrashFile();
        content.Must().Contain("first failure");
        content.Must().Contain("second failure");
    }

    [Fact]
    public void Report_shouldStillWriteTheCrashFile_whenTheLoggerThrows()
    {
        var logger = AttachLogger();
        logger.ThrowOnLog = true;

        CrashLogger.Report("Main", new InvalidOperationException("kept"));

        ReadCrashFile().Must().Contain("kept");
    }

    [Fact]
    public void Report_shouldStillLog_whenTheCrashFileCannotBeWritten()
    {
        var blocker = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocker, "x");
        CrashLogger.SetLogsDirectory(blocker);
        var logger = new RecordingLogger();
        CrashLogger.AttachLogger(logger);

        CrashLogger.Report("Main", new InvalidOperationException("disk failed"));

        logger.Entries.Must().HaveCount(1);
        logger.Entries[0].Level.Must().Be(LogLevel.Critical);
        logger.Entries[0].Message.Must().Be("Main");
    }

    [Fact]
    public void AttachLogger_shouldWriteASessionMarker()
    {
        var logger = AttachLogger();

        logger.Entries.Must().BeEmpty();
        var text = File.ReadAllText(MarkerPath()).Trim();
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _).Must().BeTrue();
    }

    [Fact]
    public void AttachLogger_shouldWarnAboutThePreviousStart_whenASessionMarkerAlreadyExists()
    {
        var marker = MarkerPath();
        File.WriteAllText(marker, " previous-start \r\n");
        var logger = new RecordingLogger();

        CrashLogger.AttachLogger(logger);

        logger.Entries.Must().HaveCount(1);
        logger.Entries[0].Level.Must().Be(LogLevel.Warning);
        logger.Entries[0].Message.Must().Contain("did not shut down cleanly");
        logger.Entries[0].Message.Must().Contain("previous-start");
        File.ReadAllText(marker).Must().NotContain("previous-start");
    }

    [Fact]
    public void AttachLogger_shouldNotThrow_whenTheSessionMarkerCannotBeWritten()
    {
        var blocker = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocker, "x");
        CrashLogger.SetLogsDirectory(blocker);
        var logger = new RecordingLogger();

        CrashLogger.AttachLogger(logger);

        logger.Entries.Must().BeEmpty();
    }

    [Fact]
    public void MarkCleanShutdown_shouldDeleteTheSessionMarker()
    {
        AttachLogger();
        CrashLogger.Report("Main", new InvalidOperationException("kept"));

        CrashLogger.MarkCleanShutdown();

        File.Exists(MarkerPath()).Must().BeFalse();
        ReadCrashFile().Must().Contain("kept");
    }

    [Fact]
    public void MarkCleanShutdown_shouldNotThrow_whenNoSessionMarkerExists()
    {
        CrashLogger.MarkCleanShutdown();

        Directory.Exists(_directory).Must().BeTrue();
        File.Exists(MarkerPath()).Must().BeFalse();
    }

    [Fact]
    public void MarkCleanShutdown_shouldLeaveTheMarker_whenItCannotBeDeleted()
    {
        var marker = MarkerPath();
        using (new FileStream(marker, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            CrashLogger.MarkCleanShutdown();

            File.Exists(marker).Must().BeTrue();
        }
    }

    [Theory]
    [InlineData(true, LogLevel.Critical, "IsTerminating=True")]
    [InlineData(false, LogLevel.Error, "IsTerminating=False")]
    public void OnUnhandledException_shouldRecordWhetherTheProcessIsTerminating(
        bool isTerminating,
        LogLevel level,
        string detail)
    {
        var logger = AttachLogger();
        var exception = new InvalidOperationException("unhandled");

        CrashLogger.OnUnhandledException(exception, isTerminating);

        logger.Entries.Must().HaveCount(1);
        logger.Entries[0].Level.Must().Be(level);
        logger.Entries[0].Exception.Must().Be(exception);
        logger.Entries[0].Message.Must().Be($"AppDomain.UnhandledException ({detail})");
        ReadCrashFile().Must().Contain($"[Crash] AppDomain.UnhandledException ({detail})");
    }

    [Fact]
    public void OnUnhandledException_shouldRecordThatNoExceptionWasSupplied_whenTheObjectIsNotAnException()
    {
        var logger = AttachLogger();

        CrashLogger.OnUnhandledException("native fault", isTerminating: true);

        logger.Entries.Must().HaveCount(1);
        logger.Entries[0].Level.Must().Be(LogLevel.Critical);
        logger.Entries[0].Exception.Must().BeNull();
        logger.Entries[0].Message.Must().Be("AppDomain.UnhandledException (IsTerminating=True)");
        ReadCrashFile().Must().Contain("(no exception object)");
    }

    [Fact]
    public void OnUnobservedTaskException_shouldReportANonFatalCrash()
    {
        var logger = AttachLogger();
        var exception = new InvalidOperationException("unobserved");

        CrashLogger.OnUnobservedTaskException(exception);

        logger.Entries.Must().HaveCount(1);
        logger.Entries[0].Level.Must().Be(LogLevel.Error);
        logger.Entries[0].Exception.Must().Be(exception);
        logger.Entries[0].Message.Must().Be("TaskScheduler.UnobservedTaskException");
        ReadCrashFile().Must().Contain("unobserved");
    }

    [Fact]
    public void OnProcessExit_shouldLogTheExitCode_whenALoggerIsAttached()
    {
        var logger = AttachLogger();

        CrashLogger.OnProcessExit(17);

        logger.Entries.Must().HaveCount(1);
        logger.Entries[0].Level.Must().Be(LogLevel.Information);
        logger.Entries[0].Exception.Must().BeNull();
        logger.Entries[0].Message.Must().Be("Process exiting (ExitCode=17).");
    }

    public void Dispose()
    {
        CrashLogger.SetLogsDirectory(null);
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A test may still be releasing the locked marker.
        }
    }

    private RecordingLogger AttachLogger()
    {
        var logger = new RecordingLogger();
        CrashLogger.AttachLogger(logger);
        return logger;
    }

    private string MarkerPath()
        => Path.Combine(_directory, "session.running");

    private string ReadCrashFile()
        => File.ReadAllText(Path.Combine(_directory, $"crash-{DateTime.Today:yyyyMMdd}.log"));

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public bool ThrowOnLog { get; set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (ThrowOnLog)
                throw new IOException("logger failed");

            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
}
