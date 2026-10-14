using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.FileSystem;

/// <summary>
/// User-facing text for filesystem failures. Classification lives in <see cref="FileSystemErrorClassifier"/>;
/// this type only renders messages, so <see cref="FileSystemError"/> is the single source of truth for
/// the kind of error that occurred.
/// </summary>
public static class FileSystemErrorMessages
{
    private const int ErrorNotSameDevice = unchecked((int)0x80070011);

    // Shell delete HRESULTs that mean "this item needs administrator rights" rather than a
    // message worth showing. E_UNEXPECTED is what IFileOperation returns when NoErrorUI is still
    // set: Exception.Message is the useless "Catastrophic failure". ERROR_CANCELLED is a declined
    // UAC prompt. The other two are an elevation that was required or refused.
    private const int EUnexpected = unchecked((int)0x8000FFFF);
    private const int ErrorCancelled = unchecked((int)0x800704C7);
    private const int ErrorElevationRequired = unchecked((int)0x800702E4);
    private const int ErrorAccessDenied = unchecked((int)0x80070005);

    public const string AdministratorPermissionRequired = "Administrator permission is required to delete this item";

    public const string RecycleBinOperationFailed = "Recycle bin operation failed.";

    /// <summary>
    /// True when a shell delete failed because administrator rights were required, declined, or
    /// suppressed. Callers must use <see cref="AdministratorPermissionRequired"/> for these and
    /// never the exception text.
    /// </summary>
    public static bool IsElevationFailure(int hresult)
        => hresult is EUnexpected or ErrorCancelled or ErrorElevationRequired or ErrorAccessDenied;

    public static string MessageForShellDeleteFailure(int? hresult)
    {
        if (hresult is int hr && IsElevationFailure(hr))
            return AdministratorPermissionRequired;

        if (hresult is null)
            return RecycleBinOperationFailed;

        return "The file operation failed";
    }

    /// <summary>
    /// Splits a delete batch into items the shell removed and items it did not.
    /// A failure HRESULT does not fail items that were already deleted.
    /// </summary>
    public static (int Succeeded, IReadOnlyList<FileOperationFailure> Failures) MapDeleteResults(
        IReadOnlyList<string> requested,
        IEnumerable<string> deleted,
        int? failureHResult)
    {
        var deletedSet = new HashSet<string>(deleted, StringComparer.OrdinalIgnoreCase);
        var message = MessageForShellDeleteFailure(failureHResult);
        var failures = new List<FileOperationFailure>();
        var succeeded = 0;

        foreach (var path in requested)
        {
            if (deletedSet.Contains(path))
                succeeded++;
            else
                failures.Add(new FileOperationFailure(path, message));
        }

        return (succeeded, failures);
    }

    /// <summary>
    /// Items that already failed with access denied and were retried through an elevated shell delete.
    /// Whatever HRESULT came back, the ones still on disk need administrator permission — not the raw code.
    /// </summary>
    public static (int Succeeded, IReadOnlyList<FileOperationFailure> Failures) MapElevationResults(
        IReadOnlyList<string> requested,
        IEnumerable<string> deleted)
        => MapDeleteResults(requested, deleted, EUnexpected);

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

/// <summary>
/// Which UI a shell delete is allowed to show.
/// </summary>
/// <remarks>
/// <see cref="ShowElevationPrompt"/> only works when <see cref="SuppressErrorUi"/> is false.
/// Leaving error UI suppressed also suppresses the UAC prompt, and the shell aborts the whole
/// batch with <c>E_UNEXPECTED</c> ("Catastrophic failure") as soon as one item needs administrator rights.
/// </remarks>
public readonly record struct DeletePromptPolicy(
    bool SuppressErrorUi,
    bool ShowElevationPrompt,
    bool RecycleOnDelete)
{
    /// <summary>Silent deletes that should not prompt, including for errors.</summary>
    public static DeletePromptPolicy Silent { get; } = new(
        SuppressErrorUi: true,
        ShowElevationPrompt: false,
        RecycleOnDelete: false);

    /// <summary>
    /// A delete that may need administrator rights. Error UI stays available so the shell can show UAC.
    /// </summary>
    public DeletePromptPolicy WithElevation(bool recycle) => new(
        SuppressErrorUi: false,
        ShowElevationPrompt: true,
        RecycleOnDelete: recycle);
}
