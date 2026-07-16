namespace HelixExplorer.Core.FileSystem;

public static class FileSystemError
{
    public static string Describe(Exception ex, string? path = null)
    {
        return ex switch
        {
            UnauthorizedAccessException => "Access denied",
            DirectoryNotFoundException => "Folder not found",
            FileNotFoundException => "Path not found",
            IOException io when IsOfflineNetworkPath(io, path) => "Network location is unavailable",
            IOException io when IsAccessDenied(io) => "Access denied",
            PathTooLongException => "Path is too long",
            NotSupportedException => "This location is not supported",
            OperationCanceledException => string.Empty,
            _ => "Could not open this location"
        };
    }

    private static bool IsOfflineNetworkPath(IOException ex, string? path)
    {
        if (!string.IsNullOrEmpty(path) && path.StartsWith(@"\\", StringComparison.Ordinal))
            return true;

        var message = ex.Message;
        return message.Contains("network", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
               || message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAccessDenied(IOException ex)
        => ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("access", StringComparison.OrdinalIgnoreCase);
}
