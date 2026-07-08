namespace HelixExplorer.Core.FileSystem;

public sealed class InternalClipboardService : IClipboardService
{
    public ClipboardPayload? Current { get; private set; }

    public bool HasPayload => Current is not null;

    public event EventHandler? Changed;

    public void SetCut(IReadOnlyList<string> paths, string sourceDirectory)
    {
        Current = new ClipboardPayload(ClipboardOperation.Cut, Snapshot(paths), sourceDirectory);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCopy(IReadOnlyList<string> paths, string sourceDirectory)
    {
        Current = new ClipboardPayload(ClipboardOperation.Copy, Snapshot(paths), sourceDirectory);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (Current is null)
            return;

        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<string> Snapshot(IReadOnlyList<string> paths)
        => paths is string[] array ? array.ToArray() : paths.ToArray();
}
