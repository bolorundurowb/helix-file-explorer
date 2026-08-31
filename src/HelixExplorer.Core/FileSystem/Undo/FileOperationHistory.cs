namespace HelixExplorer.Core.FileSystem.Undo;

/// <inheritdoc />
public sealed class FileOperationHistory : IFileOperationHistory
{
    /// <summary>
    /// Explorer keeps a bounded undo history; an unbounded one would pin paths for the process
    /// lifetime for no practical benefit, since entries this old are almost always stale anyway.
    /// </summary>
    public const int MaxEntries = 32;

    // Every mutation and every pop takes this lock. Pushes arrive from background operation
    // completions and pops from the UI thread of any window, so two windows pressing Ctrl+Z at once
    // must not receive the same batch.
    private readonly Lock _gate = new();

    private readonly LinkedList<FileOperationBatch> _undo = new();
    private readonly LinkedList<FileOperationBatch> _redo = new();

    public bool CanUndo
    {
        get
        {
            lock (_gate)
                return _undo.Count > 0;
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_gate)
                return _redo.Count > 0;
        }
    }

    public string? UndoDescription
    {
        get
        {
            lock (_gate)
                return _undo.Last?.Value.Description;
        }
    }

    public string? RedoDescription
    {
        get
        {
            lock (_gate)
                return _redo.Last?.Value.Description;
        }
    }

    public event EventHandler? Changed;

    public void Push(FileOperationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Changes.Count == 0)
            return;

        lock (_gate)
        {
            // A new forward operation invalidates everything ahead of it: the redo entries describe a
            // future that no longer follows from the current filesystem state.
            _redo.Clear();
            AddBounded(_undo, batch);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool TryPopUndo(out FileOperationBatch batch) => TryPop(_undo, out batch);

    public bool TryPopRedo(out FileOperationBatch batch) => TryPop(_redo, out batch);

    public void PushInverse(FileOperationBatch batch, bool wasUndo)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Changes.Count == 0)
            return;

        lock (_gate)
        {
            // Undoing feeds the redo stack and redoing feeds the undo stack. Routing this through
            // Push instead would clear the very stack we are trying to build up.
            AddBounded(wasUndo ? _redo : _undo, batch);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_undo.Count == 0 && _redo.Count == 0)
                return;

            _undo.Clear();
            _redo.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool TryPop(LinkedList<FileOperationBatch> stack, out FileOperationBatch batch)
    {
        lock (_gate)
        {
            var node = stack.Last;
            if (node is null)
            {
                batch = null!;
                return false;
            }

            stack.RemoveLast();
            batch = node.Value;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static void AddBounded(LinkedList<FileOperationBatch> stack, FileOperationBatch batch)
    {
        stack.AddLast(batch);

        // A linked list rather than Stack<T> precisely so the oldest entry can be dropped cheaply.
        while (stack.Count > MaxEntries)
            stack.RemoveFirst();
    }
}
