using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Logging;

public sealed class RollingFileLoggerOptions
{
    /// <summary>
    /// Application version embedded in the log directory path.
    /// </summary>
    public string Version { get; init; } = "0.0.0";

    /// <summary>
    /// Optional override for the log directory (primarily for tests).
    /// When null, uses <see cref="Infrastructure.AppPaths.GetVersionedLogsDirectory"/>.
    /// </summary>
    public string? LogsDirectory { get; init; }

    /// <summary>
    /// Maximum size of a single log file before rolling to a new segment.
    /// </summary>
    public long MaxFileSizeBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum number of rolled log files to retain in the version directory.
    /// </summary>
    public int RetainedFileCount { get; init; } = 14;

    public LogLevel MinLevel { get; init; } = LogLevel.Information;
}
