using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Infrastructure;

public interface IFileOperationControl
{
    CancellationToken CancellationToken { get; }

    void WaitIfPaused(CancellationToken cancellationToken);
}

public interface IFileOperationReporter : IFileOperationControl
{
    void Begin(FileOperationKind kind, int totalItems, string title);

    void Report(FileOperationProgress progress);

    void Complete(FileOperationKind kind, int itemCount, string message);

    void Fail(string message);

    void Cancelled(string message);
}
