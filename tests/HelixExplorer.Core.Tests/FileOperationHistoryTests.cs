using HelixExplorer.Core.FileSystem.Undo;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Core.Tests;

public class FileOperationHistoryTests
{
    private static FileOperationBatch Batch(string description, params string[] destinations)
        => new(
            UndoableOperationKind.Copy,
            description,
            [.. destinations.Select(d => new FileOperationChange($@"C:\src\{Path.GetFileName(d)}", d))]);

    [Fact]
    public void Push_StoresOnlyTheRecordedSuccesses()
    {
        // A batch that copied 3 of 5 items is recorded as those 3; undo must not invent the other 2.
        var history = new FileOperationHistory();
        history.Push(Batch("copy of 3 items", @"C:\dest\a", @"C:\dest\b", @"C:\dest\c"));

        history.TryPopUndo(out var popped).Must().BeTrue();
        popped.Changes.Count.Must().Be(3);
    }

    [Fact]
    public void Push_ThenUndo_ReturnsMostRecentBatchFirst()
    {
        var history = new FileOperationHistory();
        history.Push(Batch("first", @"C:\dest\a"));
        history.Push(Batch("second", @"C:\dest\b"));

        history.TryPopUndo(out var popped).Must().BeTrue();
        popped.Description.Must().Be("second");

        history.TryPopUndo(out var next).Must().BeTrue();
        next.Description.Must().Be("first");

        history.CanUndo.Must().BeFalse();
    }

    [Fact]
    public void Push_IgnoresBatchWithNoChanges()
    {
        var history = new FileOperationHistory();
        history.Push(new FileOperationBatch(UndoableOperationKind.Copy, "empty", []));

        history.CanUndo.Must().BeFalse();
        history.UndoDescription.Must().BeNull();
    }

    [Fact]
    public void ForwardPush_ClearsRedoStack()
    {
        var history = new FileOperationHistory();
        history.Push(Batch("first", @"C:\dest\a"));
        history.TryPopUndo(out var popped).Must().BeTrue();
        history.PushInverse(popped, wasUndo: true);

        history.CanRedo.Must().BeTrue();

        // The redo entry describes a future that no longer follows from the new state.
        history.Push(Batch("newer", @"C:\dest\c"));

        history.CanRedo.Must().BeFalse();
        history.CanUndo.Must().BeTrue();
    }

    [Fact]
    public void PushInverse_DoesNotClearOppositeStack()
    {
        var history = new FileOperationHistory();
        history.Push(Batch("a", @"C:\dest\a"));
        history.Push(Batch("b", @"C:\dest\b"));

        history.TryPopUndo(out var undone).Must().BeTrue();
        history.PushInverse(undone, wasUndo: true);

        // Undoing "b" must leave "a" available to undo next, not wipe it.
        history.CanUndo.Must().BeTrue();
        history.UndoDescription.Must().Be("a");
        history.RedoDescription.Must().Be("b");
    }

    [Fact]
    public void Push_DropsOldestBeyondCap()
    {
        var history = new FileOperationHistory();
        for (var i = 0; i < FileOperationHistory.MaxEntries + 5; i++)
            history.Push(Batch($"op{i}", $@"C:\dest\{i}"));

        var seen = new List<string>();
        while (history.TryPopUndo(out var batch))
            seen.Add(batch.Description);

        seen.Count.Must().Be(FileOperationHistory.MaxEntries);
        seen[0].Must().Be($"op{FileOperationHistory.MaxEntries + 4}");

        // The five oldest were evicted rather than the newest being rejected.
        seen.Must().NotContain("op0");
    }

    [Fact]
    public void ConcurrentPops_EachBatchHandedOutOnce()
    {
        var history = new FileOperationHistory();
        const int count = 200;
        for (var i = 0; i < count; i++)
            history.Push(Batch($"op{i}", $@"C:\dest\{i}"));

        // Two windows pressing Ctrl+Z at the same instant must not both receive the same batch and
        // apply the same inverse twice.
        var popped = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 8, _ =>
        {
            while (history.TryPopUndo(out var batch))
                popped.Add(batch.Description);
        });

        popped.Count.Must().Be(FileOperationHistory.MaxEntries);
        popped.Distinct().Count().Must().Be(FileOperationHistory.MaxEntries);
    }

    [Fact]
    public void Changed_RaisedOnPushAndPop()
    {
        var history = new FileOperationHistory();
        var raised = 0;
        history.Changed += (_, _) => raised++;

        history.Push(Batch("a", @"C:\dest\a"));
        history.TryPopUndo(out _);

        raised.Must().Be(2);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var history = new FileOperationHistory();
        history.Push(Batch("a", @"C:\dest\a"));
        history.TryPopUndo(out var popped);
        history.PushInverse(popped, wasUndo: true);

        history.Clear();

        history.CanUndo.Must().BeFalse();
        history.CanRedo.Must().BeFalse();
    }
}
