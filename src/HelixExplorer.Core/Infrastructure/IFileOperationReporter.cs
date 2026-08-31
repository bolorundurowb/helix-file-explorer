using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Infrastructure;

public interface IFileOperationControl
{
    CancellationToken CancellationToken { get; }

    void WaitIfPaused(CancellationToken cancellationToken);
}

public interface IFileOperationReporter : IFileOperationControl
{
    /// <summary>
    /// True while an operation is in flight in this reporter's window.
    /// </summary>
    /// <remarks>
    /// On the interface rather than the concrete reporter because undo has to refuse to start while a
    /// forward operation is still running: applying an inverse against a half-written destination
    /// would act on paths the running batch is still changing.
    /// </remarks>
    bool IsBusy { get; }

    void Begin(FileOperationKind kind, int totalItems, string title);

    void Report(FileOperationProgress progress);

    void Complete(FileOperationKind kind, int itemCount, string message);

    void Fail(string message);

    void Cancelled(string message);
}
