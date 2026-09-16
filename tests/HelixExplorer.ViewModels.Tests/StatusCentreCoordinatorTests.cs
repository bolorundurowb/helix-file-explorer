using HelixExplorer.Core.FileSystem;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;

namespace HelixExplorer.ViewModels.Tests;

public class StatusCentreCoordinatorTests
{
    [Fact]
    public void Begin_AutoOpensCard()
    {
        var reporter = new FileOperationReporter();
        using var centre = new StatusCentreCoordinator(reporter, () => false);

        reporter.Begin(FileOperationKind.Copy, 2, "Copying");

        centre.IsOpen.Must().BeTrue();
    }

    [Fact]
    public void Begin_DoesNotOpenWhileCommandPaletteIsOpen()
    {
        var reporter = new FileOperationReporter();
        using var centre = new StatusCentreCoordinator(reporter, () => true);

        reporter.Begin(FileOperationKind.Copy, 2, "Copying");

        centre.IsOpen.Must().BeFalse();
    }

    [Fact]
    public void CloseDuringActive_DoesNotReopenUntilNextBegin()
    {
        var reporter = new FileOperationReporter();
        using var centre = new StatusCentreCoordinator(reporter, () => false);
        reporter.Begin(FileOperationKind.Copy, 2, "Copying");
        centre.Close();

        reporter.Report(new FileOperationProgress(1, 2, @"C:\a.txt", FileOperationKind.Copy));
        centre.IsOpen.Must().BeFalse();

        reporter.Complete(FileOperationKind.Copy, 2, "Copied 2 items");
        centre.IsOpen.Must().BeFalse();

        reporter.Begin(FileOperationKind.Move, 1, "Moving");
        centre.IsOpen.Must().BeTrue();
    }

    [Fact]
    public void Success_AutoDismissesWhenTimerFires()
    {
        var reporter = new FileOperationReporter();
        var scheduler = new ManualStatusCentreScheduler();
        using var centre = new StatusCentreCoordinator(reporter, () => false, scheduler);

        reporter.Begin(FileOperationKind.Copy, 1, "Copying");
        reporter.Complete(FileOperationKind.Copy, 1, "Copied 1 item");
        centre.IsOpen.Must().BeTrue();
        scheduler.Callback.Must().NotBeNull();

        scheduler.Run();

        centre.IsOpen.Must().BeFalse();
    }

    [Fact]
    public void Fail_KeepsCardOpenAndDoesNotScheduleDismiss()
    {
        var reporter = new FileOperationReporter();
        var scheduler = new ManualStatusCentreScheduler();
        using var centre = new StatusCentreCoordinator(reporter, () => false, scheduler);

        reporter.Begin(FileOperationKind.Delete, 1, "Deleting");
        reporter.Fail("Delete failed");

        centre.IsOpen.Must().BeTrue();
        scheduler.Callback.Must().BeNull();
    }

    [Fact]
    public void FailWhileClosed_SetsUnreadFailureBadge()
    {
        var reporter = new FileOperationReporter();
        using var centre = new StatusCentreCoordinator(reporter, () => false);
        reporter.Begin(FileOperationKind.Delete, 1, "Deleting");
        centre.Close();

        reporter.Fail("Delete failed");

        centre.IsOpen.Must().BeFalse();
        centre.HasUnreadFailure.Must().BeTrue();
    }

    [Fact]
    public void Pin_PreventsAutoDismiss()
    {
        var reporter = new FileOperationReporter();
        var scheduler = new ManualStatusCentreScheduler();
        using var centre = new StatusCentreCoordinator(reporter, () => false, scheduler);

        reporter.Begin(FileOperationKind.Copy, 1, "Copying");
        centre.TogglePinCommand.Execute(null);
        reporter.Complete(FileOperationKind.Copy, 1, "Copied 1 item");

        scheduler.Callback.Must().BeNull();
        centre.IsOpen.Must().BeTrue();
    }

    [Fact]
    public void Hover_CancelsAutoDismissUntilPointerLeaves()
    {
        var reporter = new FileOperationReporter();
        var scheduler = new ManualStatusCentreScheduler();
        using var centre = new StatusCentreCoordinator(reporter, () => false, scheduler);

        reporter.Begin(FileOperationKind.Copy, 1, "Copying");
        reporter.Complete(FileOperationKind.Copy, 1, "Copied 1 item");
        centre.SetHovered(true);

        scheduler.Callback.Must().BeNull();

        centre.SetHovered(false);
        scheduler.Callback.Must().NotBeNull();
        scheduler.Run();
        centre.IsOpen.Must().BeFalse();
    }

    [Fact]
    public void OpenFromIdle_DoesNotAutoDismiss()
    {
        var reporter = new FileOperationReporter();
        var scheduler = new ManualStatusCentreScheduler();
        using var centre = new StatusCentreCoordinator(reporter, () => false, scheduler);
        reporter.Complete(FileOperationKind.Copy, 1, "Copied 1 item");

        centre.Toggle();

        centre.IsOpen.Must().BeTrue();
        scheduler.Callback.Must().BeNull();
    }

    private sealed class ManualStatusCentreScheduler : IStatusCentreScheduler
    {
        public Action? Callback { get; private set; }

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            Callback = callback;
            return new Disposer(() => Callback = null);
        }

        public void Run()
        {
            var callback = Callback;
            Callback = null;
            callback?.Invoke();
        }

        private sealed class Disposer(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
