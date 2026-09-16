namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// User-facing text for filesystem failures. Classification lives in <see cref="FileSystemErrorClassifier"/>;
/// this type only renders messages, so <see cref="FileSystemError"/> is the single source of truth for
/// the kind of error that occurred.
/// </summary>
public static class FileSystemErrorMessages
{
    private const int ErrorNotSameDevice = unchecked((int)0x80070011);

    public static string Describe(Exception exception, string? path = null)
    {
        if (exception is OperationCanceledException)
            return string.Empty;

        if (exception is DirectoryNotFoundException)
            return "Folder not found";

        return Describe(FileSystemErrorClassifier.FromException(exception, path));
    }

    public static string Describe(FileSystemError error) => error switch
    {
        FileSystemError.PathNotFound => "Path not found",
        FileSystemError.AccessDenied => "Access denied",
        FileSystemError.FileInUse => "The file is in use by another program",
        FileSystemError.DiskFull => "Not enough disk space",
        FileSystemError.PathTooLong => "Path is too long",
        FileSystemError.NetworkUnavailable => "Network location is unavailable",
        FileSystemError.NotSupported => "This location is not supported",
        _ => "Could not open this location",
    };

    public static string DescribeFileOperation(Exception exception, string? path = null)
    {
        if (exception is OperationCanceledException)
            return string.Empty;

        if (exception is DirectoryNotFoundException)
            return "Folder not found";

        if (exception is InvalidOperationException invalid && !string.IsNullOrWhiteSpace(invalid.Message))
            return invalid.Message;

        if (exception is IOException io && IsSameRootMoveFailure(io))
            return "Cannot move this folder across drives or network locations";

        return DescribeFileOperation(FileSystemErrorClassifier.FromException(exception, path), exception);
    }

    public static string DescribeFileOperation(FileSystemError error, Exception? exception = null) => error switch
    {
        FileSystemError.PathNotFound => "Path not found",
        FileSystemError.AccessDenied => "Access denied",
        FileSystemError.FileInUse => "The file is in use by another program",
        FileSystemError.DiskFull => "Not enough disk space",
        FileSystemError.PathTooLong => "Path is too long",
        FileSystemError.NetworkUnavailable => "Network location is unavailable",
        FileSystemError.NotSupported => "This location is not supported",
        FileSystemError.Unknown when exception is IOException io && !string.IsNullOrWhiteSpace(io.Message) => io.Message,
        _ => "The file operation failed",
    };

    private static bool IsSameRootMoveFailure(IOException ex)
    {
        if (ex.HResult == ErrorNotSameDevice)
            return true;

        var message = ex.Message;
        return message.Contains("same root", StringComparison.OrdinalIgnoreCase)
               || message.Contains("must have the same root", StringComparison.OrdinalIgnoreCase);
    }
}
