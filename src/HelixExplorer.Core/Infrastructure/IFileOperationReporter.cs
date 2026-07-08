using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Infrastructure;

public interface IFileOperationReporter
{
    void Begin(FileOperationKind kind, int totalItems, string title);

    void Report(FileOperationProgress progress);

    void Complete(FileOperationKind kind, int itemCount, string message);

    void Fail(string message);
}
