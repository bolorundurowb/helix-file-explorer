using System.Collections.Concurrent;
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

        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        var line = exception is null
            ? $"{timestamp} [{logLevel}] {categoryName}: {message}"
            : $"{timestamp} [{logLevel}] {categoryName}: {message}{Environment.NewLine}{exception}";

        lock (_writeLock)
        {
            if (_disposed)
                return;

            EnsureWriter();
            _writer!.WriteLine(line);
            _writer.Flush();
            RollIfNeeded();
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
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
        _currentFilePath = path;

        if (isNewFile)
        {
            _writer.WriteLine($"# Helix Explorer log — version {_options.Version}");
            _writer.WriteLine($"# Started {DateTimeOffset.Now:O}");
            _writer.Flush();
        }

        PruneOldFiles();
    }

    private void RollIfNeeded()
    {
        if (_currentFilePath is null || _writer is null)
            return;

        _writer.Flush();
        if (new FileInfo(_currentFilePath).Length < _options.MaxFileSizeBytes)
            return;

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
        var files = Directory.EnumerateFiles(_directory, "helix-explorer-*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Skip(_options.RetainedFileCount)
            .ToArray();

        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Best-effort cleanup; logging must not throw.
            }
        }
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
        _currentFilePath = null;
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
