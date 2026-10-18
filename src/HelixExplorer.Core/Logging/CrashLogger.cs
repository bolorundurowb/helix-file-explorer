using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Logging;

/// <summary>
/// Captures crashes that would otherwise vanish: unhandled/unobserved exceptions, failures before the
/// DI host exists (written straight to a crash file) and sessions that ended without a clean shutdown.
/// </summary>
public static class CrashLogger
{
    private const string SessionMarkerName = "session.running";
    private static readonly object FileLock = new();
    private static ILogger? _logger;
    private static bool _installed;

    // Tests redirect crash files and the session marker. Production leaves this unset.
    private static string? _logsDirectoryOverride;

    /// <summary>Hooks process-wide handlers. Safe to call before the logging host exists.</summary>
    public static void InstallEarly()
    {
        if (_installed)
            return;

        _installed = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            OnUnhandledException(e.ExceptionObject, e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            OnUnobservedTaskException(e.Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => OnProcessExit(Environment.ExitCode);
    }

    /// <summary>
    /// Redirects crash files and the session marker, and drops any attached logger.
    /// Pass null to use the versioned logs directory again.
    /// </summary>
    internal static void SetLogsDirectory(string? directory)
    {
        _logsDirectoryOverride = directory;
        _logger = null;
    }

    internal static void OnUnhandledException(object? exceptionObject, bool isTerminating)
        => Report(
            "AppDomain.UnhandledException",
            exceptionObject as Exception,
            $"IsTerminating={isTerminating}",
            fatal: isTerminating);

    internal static void OnUnobservedTaskException(Exception exception)
        => Report("TaskScheduler.UnobservedTaskException", exception, null, fatal: false);

    internal static void OnProcessExit(int exitCode)
        => _logger?.LogInformation("Process exiting (ExitCode={ExitCode}).", exitCode);

    /// <summary>Routes subsequent reports to the app logger and checks for a previous unclean exit.</summary>
    public static void AttachLogger(ILogger logger)
    {
        _logger = logger;

        try
        {
            var marker = MarkerPath();
            if (File.Exists(marker))
            {
                logger.LogWarning(
                    "Previous session did not shut down cleanly (started {Started}). Check crash-*.log for details; native crashes leave no managed trace.",
                    File.ReadAllText(marker).Trim());
            }

            File.WriteAllText(marker, DateTimeOffset.Now.ToString("O"));
        }
        catch
        {
            // Diagnostics must never break startup.
        }
    }

    /// <summary>Call on orderly shutdown so the next launch knows the session ended normally.</summary>
    public static void MarkCleanShutdown()
    {
        try
        {
            var marker = MarkerPath();
            if (File.Exists(marker))
                File.Delete(marker);
        }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>Logs an exception to the app log (if attached) and always to the crash file.</summary>
    public static void Report(string source, Exception? exception, string? detail = null, bool fatal = true)
    {
        var message = $"{source}{(detail is null ? "" : $" ({detail})")}";

        try
        {
            _logger?.Log(fatal ? LogLevel.Critical : LogLevel.Error, exception, "{Message}", message);
        }
        catch
        {
            // Fall through to the crash file.
        }

        WriteCrashFile(message, exception);
    }

    private static void WriteCrashFile(string message, Exception? exception)
    {
        try
        {
            var directory = LogsDirectory();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"crash-{DateTime.Today:yyyyMMdd}.log");
            var text = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [Crash] {message}{Environment.NewLine}" +
                       $"{exception?.ToString() ?? "(no exception object)"}{Environment.NewLine}{Environment.NewLine}";
            lock (FileLock)
                File.AppendAllText(path, text);
        }
        catch
        {
            // Nothing more we can do.
        }
    }

    private static string LogsDirectory()
        => _logsDirectoryOverride ?? AppPaths.GetVersionedLogsDirectory(AppVersion.Current);

    private static string MarkerPath()
        => Path.Combine(LogsDirectory(), SessionMarkerName);
}
