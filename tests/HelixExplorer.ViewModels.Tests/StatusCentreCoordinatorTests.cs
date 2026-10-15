using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
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

    [Fact]
    public void FailWithoutBegin_SetsUnreadFailureBadgeAndStaysInList()
    {
        var reporter = new FileOperationReporter();
        var scheduler = new ManualStatusCentreScheduler();
        using var centre = new StatusCentreCoordinator(reporter, () => false, scheduler);

        reporter.Fail("Could not open terminal");

        centre.IsOpen.Must().BeFalse();
        centre.HasUnreadFailure.Must().BeTrue();
        reporter.Completed.Count.Must().Be(1);
        scheduler.Callback.Must().BeNull();
    }

    [Fact]
    public void Note_ShowsBesideChipAndClearsWhenTimerFires()
    {
        var reporter = new FileOperationReporter();
        var scheduler = new ManualStatusCentreScheduler();
        using var centre = new StatusCentreCoordinator(reporter, () => false, scheduler);

        reporter.Note("Path copied");

        centre.Note.Must().Be("Path copied");
        centre.HasNote.Must().BeTrue();
        scheduler.LastDelay.Must().Be(StatusCentreCoordinator.NoteDuration);
        centre.IsOpen.Must().BeFalse();
        reporter.Completed.Count.Must().Be(0);

        scheduler.Run();

        centre.HasNote.Must().BeFalse();
    }

    [Fact]
    public void Attention_TracksProgressAndClearsOnSuccess()
    {
        var reporter = new FileOperationReporter();
        var attention = new RecordingAttention();
        using var centre = new StatusCentreCoordinator(reporter, () => false, new ManualStatusCentreScheduler(), attention);

        reporter.Begin(FileOperationKind.Copy, 0, "Copying");
        attention.Calls.Must().Contain("progress:null");

        reporter.Report(new FileOperationProgress(1, 2, @"C:\a.txt", FileOperationKind.Copy));
        attention.Calls.Must().Contain("progress:0.5");

        reporter.Complete(FileOperationKind.Copy, 2, "Copied 2 items");
        attention.Calls[^2].Must().Be("clear");
        attention.Calls[^1].Must().Be("notify:Copied 2 items:False");
    }

    [Fact]
    public void Attention_FailureShowsErrorUntilCardIsOpened()
    {
        var reporter = new FileOperationReporter();
        var attention = new RecordingAttention();
        using var centre = new StatusCentreCoordinator(reporter, () => false, new ManualStatusCentreScheduler(), attention);
        reporter.Begin(FileOperationKind.Delete, 1, "Deleting");
        centre.Close();

        reporter.Fail("Delete failed");

        attention.Calls.Must().Contain("error");
        attention.Calls[^1].Must().Be("notify:Delete failed:True");

        centre.Toggle();

        attention.Calls[^1].Must().Be("clear");
    }

    [Fact]
    public void Attention_CancelClearsWithoutToast()
    {
        var reporter = new FileOperationReporter();
        var attention = new RecordingAttention();
        using var centre = new StatusCentreCoordinator(reporter, () => false, new ManualStatusCentreScheduler(), attention);
        reporter.Begin(FileOperationKind.Copy, 1, "Copying");

        reporter.Cancelled("Operation cancelled");

        attention.Calls[^1].Must().Be("clear");
        attention.Calls.Any(c => c.StartsWith("notify:", StringComparison.Ordinal)).Must().BeFalse();
    }

    private sealed class RecordingAttention : IAttentionService
    {
        public List<string> Calls { get; } = [];

        public void ShowProgress(double? fraction)
            => Calls.Add(fraction is { } f ? $"progress:{f.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "progress:null");

        public void ShowError() => Calls.Add("error");

        public void ClearProgress() => Calls.Add("clear");

        public void NotifyIfInactive(string title, string message, bool isError) => Calls.Add($"notify:{message}:{isError}");

        public void FlashIfInactive() => Calls.Add("flash");
    }

    private sealed class ManualStatusCentreScheduler : IStatusCentreScheduler
    {
        public Action? Callback { get; private set; }

        public TimeSpan LastDelay { get; private set; }

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            Callback = callback;
            LastDelay = delay;
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
