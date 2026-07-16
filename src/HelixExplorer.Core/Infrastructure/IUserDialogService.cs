namespace HelixExplorer.Core.Infrastructure;

public enum FileConflictChoice
{
    Replace,
    KeepBoth,
    Skip,
    Cancel
}

public sealed record FileConflictInfo(string SourcePath, string DestinationPath, bool IsDirectory);

public sealed record FileConflictResolution(FileConflictChoice Choice, bool ApplyToAll);

public sealed record FileOperationFailure(string Path, string Message);

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
/// Kept separate from <see cref="IUiHost"/> which remains HWND/shell specific.
/// </summary>
public interface IUserDialogService
{
    Task<bool> ConfirmAsync(string title, string message);

    Task ShowErrorAsync(string title, string message);

    Task ShowOperationSummaryAsync(FileOperationResult result, string operationName);

    Task<FileConflictResolution?> ResolveConflictAsync(FileConflictInfo conflict, bool canApplyToAll);
}
