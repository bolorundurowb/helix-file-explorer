using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Logging;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly RollingFileLoggerOptions _options;
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly object _writeLock = new();
    private readonly string _directory;
    private StreamWriter? _writer;
    private string? _currentFilePath;
    private long _currentFileBytes;
    private int _linesSinceFlush;
    private bool _disposed;

    public RollingFileLoggerProvider(RollingFileLoggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxFileSizeBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxFileSizeBytes must be at least 1 KB.");
        if (options.RetainedFileCount < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "RetainedFileCount must be at least 1.");

        _options = options;
        _directory = options.LogsDirectory ?? AppPaths.GetVersionedLogsDirectory(options.Version);
        Directory.CreateDirectory(_directory);
    }

    public string LogsDirectory => _directory;

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _loggers.GetOrAdd(categoryName, static (name, provider) => new RollingFileLogger(name, provider), this);
    }

    internal bool IsEnabled(LogLevel logLevel) => logLevel >= _options.MinLevel && logLevel != LogLevel.None;

    internal void Write(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception)
    {
        if (_disposed || !IsEnabled(logLevel))
            return;

        // Invariant culture: a log read on a machine with a different locale than the one that wrote
        // it must still parse, and support tooling greps these timestamps.
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var line = exception is null
            ? $"{timestamp} [{logLevel}] {categoryName}: {message}"
            : $"{timestamp} [{logLevel}] {categoryName}: {message}{Environment.NewLine}{exception}";

        lock (_writeLock)
        {
            if (_disposed)
                return;

            // Deliberately broad: a logging call must never throw (see comment above - this can run
            // from inside arbitrary catch blocks throughout the app), so every possible failure mode
            // of the writer/filesystem needs to be swallowed here, not just a chosen subset.
#pragma warning disable CA1031
            try
            {
                // A logging call must never throw: Log(...) is invoked from arbitrary call sites
                // throughout the app, including catch blocks, and a failure here (disk full, log
                // directory removed out from under the process, antivirus holding a lock, ...) must
                // not crash whatever unrelated code happened to log something.
                EnsureWriter();
                _writer!.WriteLine(line);
                _currentFileBytes += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                _linesSinceFlush++;
                if (_linesSinceFlush >= 16 || logLevel >= LogLevel.Warning)
                {
                    _writer.Flush();
                    _linesSinceFlush = 0;
                }
                RollIfNeeded();
            }
            catch
            {
                // Best-effort: drop the line rather than propagate. Force a reopen attempt next
                // call instead of retrying the same (possibly still-broken) writer indefinitely.
                CloseWriter();
            }
#pragma warning restore CA1031
        }
    }

    private void EnsureWriter()
    {
        var desiredPath = GetActiveFilePath(DateTime.Today);
        if (_writer is not null &&
            _currentFilePath is not null &&
            string.Equals(_currentFilePath, desiredPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CloseWriter();
        OpenWriter(desiredPath);
    }

    private void OpenWriter(string path)
    {
        var isNewFile = !File.Exists(path) || new FileInfo(path).Length == 0;
        // FileShare.Delete: the user (or a cleanup tool) deleting a log file while it is the active
        // one should not be blocked just because this process still holds it open.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
        _currentFilePath = path;
        _currentFileBytes = stream.Length;
        _linesSinceFlush = 0;

        if (isNewFile)
        {
            _writer.WriteLine($"# Helix Explorer log — version {_options.Version}");
            _writer.WriteLine($"# Started {DateTimeOffset.Now:O}");
            _writer.Flush();
            _currentFileBytes = stream.Length;
        }

        PruneOldFiles();
    }

    private void RollIfNeeded()
    {
        if (_currentFilePath is null || _writer is null)
            return;

        if (_currentFileBytes < _options.MaxFileSizeBytes)
            return;

        _writer.Flush();
        var pathToRoll = _currentFilePath;
        var directory = Path.GetDirectoryName(pathToRoll)!;
        var baseName = Path.GetFileNameWithoutExtension(pathToRoll);
        var extension = Path.GetExtension(pathToRoll);
        var nextIndex = 1;
        string rolledPath;
        do
        {
            rolledPath = Path.Combine(directory, $"{baseName}.{nextIndex}{extension}");
            nextIndex++;
        } while (File.Exists(rolledPath));

        CloseWriter();
        File.Move(pathToRoll, rolledPath);
        OpenWriter(GetActiveFilePath(DateTime.Today));
    }

    private string GetActiveFilePath(DateTime date)
        => Path.Combine(_directory, $"helix-explorer-{date:yyyyMMdd}.log");

    private void PruneOldFiles()
    {
        // RetainedFileCount applies to rolled segments only; never prune the active daily file.
        var activePath = _currentFilePath ?? GetActiveFilePath(DateTime.Today);
        var files = Directory.EnumerateFiles(_directory, "helix-explorer-*.log")
            .Where(path => !string.Equals(path, activePath, StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Skip(_options.RetainedFileCount)
            .ToArray();

        foreach (var file in files)
        {
            // Deliberately broad: same "logging must never throw" rationale as Write() above -
            // pruning is opportunistic cleanup, and a locked/already-gone file must not propagate.
#pragma warning disable CA1031
            try
            {
                file.Delete();
            }
            catch
            {
                // Best-effort cleanup; logging must not throw.
            }
#pragma warning restore CA1031
        }
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
        _currentFilePath = null;
        _currentFileBytes = 0;
        _linesSinceFlush = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_writeLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            CloseWriter();
            _loggers.Clear();
        }
    }

    private sealed class RollingFileLogger(string categoryName, RollingFileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            ArgumentNullException.ThrowIfNull(formatter);
            provider.Write(categoryName, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
