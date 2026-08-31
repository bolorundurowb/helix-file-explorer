namespace HelixExplorer.Core.FileSystem;

public static class FileSystemError
{
    // Win32 error codes as they appear on IOException.HResult (HRESULT_FROM_WIN32: 0x8007xxxx).
    // .NET's own Win32-originated IOExceptions carry the real code here regardless of the OS
    // language, unlike ex.Message, which is only in English on an English-language Windows
    // install - the string checks below only ever matched on such a machine and silently fell
    // through to the generic fallback on any other locale.
    private const int ErrorAccessDenied = unchecked((int)0x80070005);
    private const int ErrorNotSameDevice = unchecked((int)0x80070011);
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorBadNetpath = unchecked((int)0x80070035);
    private const int ErrorNetnameDeleted = unchecked((int)0x80070040);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorSemTimeout = unchecked((int)0x80070079);

    public static string Describe(Exception ex, string? path = null)
    {
        return ex switch
        {
            UnauthorizedAccessException => "Access denied",
            DirectoryNotFoundException => "Folder not found",
            FileNotFoundException => "Path not found",
            IOException io when IsOfflineNetworkPath(io, path) => "Network location is unavailable",
            IOException io when IsAccessDenied(io) => "Access denied",
            IOException io when IsDiskFull(io) => "Not enough disk space",
            PathTooLongException => "Path is too long",
            NotSupportedException => "This location is not supported",
            OperationCanceledException => string.Empty,
            _ => "Could not open this location"
        };
    }

    /// <summary>
    /// User-facing text for copy/move/delete failures. Distinct from <see cref="Describe"/>, which
    /// is for opening a location.
    /// </summary>
    public static string DescribeFileOperation(Exception ex, string? path = null)
    {
        return ex switch
        {
            UnauthorizedAccessException => "Access denied",
            DirectoryNotFoundException => "Folder not found",
            FileNotFoundException => "Path not found",
            PathTooLongException => "Path is too long",
            OperationCanceledException => string.Empty,
            InvalidOperationException inv when !string.IsNullOrWhiteSpace(inv.Message) => inv.Message,
            IOException io when IsSameRootMoveFailure(io) =>
                "Cannot move this folder across drives or network locations",
            IOException io when IsOfflineNetworkPath(io, path) => "Network location is unavailable",
            IOException io when IsAccessDenied(io) => "Access denied",
            IOException io when IsDiskFull(io) => "Not enough disk space",
            IOException io when io.HResult is ErrorSharingViolation or ErrorLockViolation =>
                "The file is in use by another program",
            IOException io => string.IsNullOrWhiteSpace(io.Message)
                ? "The file operation failed"
                : io.Message,
            _ => "The file operation failed"
        };
    }

    private static bool IsSameRootMoveFailure(IOException ex)
    {
        if (ex.HResult == ErrorNotSameDevice)
            return true;

        var message = ex.Message;
        return message.Contains("same root", StringComparison.OrdinalIgnoreCase)
               || message.Contains("must have the same root", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOfflineNetworkPath(IOException ex, string? path)
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

    private static bool IsAccessDenied(IOException ex)
        => ex.HResult == ErrorAccessDenied
           || ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("access", StringComparison.OrdinalIgnoreCase);

    private static bool IsDiskFull(IOException ex)
        => ex.HResult is ErrorHandleDiskFull or ErrorDiskFull
           || ex.Message.Contains("disk full", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("not enough space", StringComparison.OrdinalIgnoreCase);
}
