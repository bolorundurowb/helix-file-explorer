namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// Expected, user-actionable filesystem failure kinds. Unexpected software faults (bugs) should still
/// throw; cancellation is deliberately absent so callers observe <see cref="OperationCanceledException"/>
/// or the token directly. Used as the error payload of <c>Result&lt;T, FileSystemError&gt;</c>.
/// </summary>
public enum FileSystemError
{
    PathNotFound,
    AccessDenied,
    FileInUse,
    DiskFull,
    PathTooLong,
    NetworkUnavailable,
    NotSupported,
    Unknown,
}

public static class FileSystemErrorClassifier
{
    // Win32 error codes as carried on IOException.HResult (HRESULT_FROM_WIN32: 0x8007xxxx). Matching
    // the HResult is locale-independent, unlike ex.Message which is localized on a non-English Windows.
    private const int ErrorAccessDenied = unchecked((int)0x80070005);
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorBadNetpath = unchecked((int)0x80070035);
    private const int ErrorNetnameDeleted = unchecked((int)0x80070040);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorSemTimeout = unchecked((int)0x80070079);

    /// <summary>
    /// Classifies an exception as a <see cref="FileSystemError"/>. Unknown exceptions map to
    /// <see cref="FileSystemError.Unknown"/>; callers should treat cancellation separately.
    /// </summary>
    public static FileSystemError FromException(Exception exception, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            UnauthorizedAccessException => FileSystemError.AccessDenied,
            DirectoryNotFoundException => FileSystemError.PathNotFound,
            FileNotFoundException => FileSystemError.PathNotFound,
            PathTooLongException => FileSystemError.PathTooLong,
            NotSupportedException => FileSystemError.NotSupported,
            IOException io when IsFileInUse(io) => FileSystemError.FileInUse,
            IOException io when IsDiskFull(io) => FileSystemError.DiskFull,
            IOException io when IsNetworkUnavailable(io, path) => FileSystemError.NetworkUnavailable,
            IOException io when IsAccessDenied(io) => FileSystemError.AccessDenied,
            _ => FileSystemError.Unknown,
        };
    }

    private static bool IsAccessDenied(IOException ex)
        => ex.HResult == ErrorAccessDenied
           || ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("access", StringComparison.OrdinalIgnoreCase);

    private static bool IsFileInUse(IOException ex)
        => ex.HResult is ErrorSharingViolation or ErrorLockViolation;

    private static bool IsDiskFull(IOException ex)
        => ex.HResult is ErrorHandleDiskFull or ErrorDiskFull
           || ex.Message.Contains("disk full", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("not enough space", StringComparison.OrdinalIgnoreCase);

    private static bool IsNetworkUnavailable(IOException ex, string? path)
    {
        if (ex.HResult is ErrorBadNetpath or ErrorNetnameDeleted or ErrorSemTimeout)
            return true;

        if (!string.IsNullOrEmpty(path) && path.StartsWith(@"\\", StringComparison.Ordinal))
            return true;

        var message = ex.Message;
        return message.Contains("network", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
               || message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }
}
