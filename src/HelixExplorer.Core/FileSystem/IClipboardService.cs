namespace HelixExplorer.Core.FileSystem;

public interface IClipboardService
{
    ClipboardPayload? Current { get; }

    bool HasPayload { get; }

    event EventHandler? Changed;

    void SetCut(IReadOnlyList<string> paths, string sourceDirectory);

    void SetCopy(IReadOnlyList<string> paths, string sourceDirectory);

    void Clear();
}
