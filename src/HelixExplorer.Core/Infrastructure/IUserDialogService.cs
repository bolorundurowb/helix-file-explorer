namespace HelixExplorer.Core.Infrastructure;

/// <summary>Choices a user can make when resolving a copy/move name conflict.</summary>
public enum FileConflictChoice
{
    Replace,
    KeepBoth,
    Skip,
    Cancel
}

/// <summary>Describes a single name collision encountered during a copy/move batch.</summary>
public sealed record FileConflictInfo(string SourcePath, string DestinationPath, bool IsDirectory);

/// <summary>User's response to a conflict prompt, optionally applying to all remaining conflicts.</summary>
public sealed record FileConflictResolution(FileConflictChoice Choice, bool ApplyToAll);

/// <summary>Describes a single item that failed within a file-operation batch.</summary>
public sealed record FileOperationFailure(string Path, string Message);

/// <summary>Structured outcome of a copy/move/delete batch: counts plus per-path failures.</summary>
public sealed record FileOperationResult(
    int Succeeded,
    int Skipped,
    int Failed,
    IReadOnlyList<FileOperationFailure> Failures)
{
    public static FileOperationResult Empty { get; } = new(0, 0, 0, Array.Empty<FileOperationFailure>());

    public bool HasIssues => Failed > 0 || Skipped > 0;

    public static FileOperationResult operator +(FileOperationResult a, FileOperationResult b)
    {
        var failures = new List<FileOperationFailure>(a.Failures.Count + b.Failures.Count);
        failures.AddRange(a.Failures);
        failures.AddRange(b.Failures);
        return new FileOperationResult(
            a.Succeeded + b.Succeeded,
            a.Skipped + b.Skipped,
            a.Failed + b.Failed,
            failures);
    }
}

/// <summary>
/// UI-platform bridge for user-facing dialogs (confirm, error, conflict, summary).
/// Kept separate from <see cref="IUiHost"/> which remains HWND/shell specific.
/// </summary>
public interface IUserDialogService
{
    Task<bool> ConfirmAsync(string title, string message);

    Task ShowErrorAsync(string title, string message);

    Task ShowOperationSummaryAsync(FileOperationResult result, string operationName);

    Task<FileConflictResolution?> ResolveConflictAsync(FileConflictInfo conflict, bool canApplyToAll);
}
