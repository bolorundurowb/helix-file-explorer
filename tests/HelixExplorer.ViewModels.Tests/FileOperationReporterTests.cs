using HelixExplorer.Core.FileSystem;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels.Tests;

public class FileOperationReporterTests
{
    [Fact]
    public void Begin_resets_progress_and_marks_active()
    {
        var reporter = new FileOperationReporter();

        reporter.Begin(FileOperationKind.Copy, totalItems: 4, title: "Copying items");

        reporter.HasActive.Must().BeTrue();
        reporter.IsBusy.Must().BeTrue();
        reporter.Progress.Must().Be(0);
        reporter.IsIndeterminate.Must().BeFalse();
        reporter.ActiveTitle.Must().Be("Copying items");
        reporter.CanPauseOperation.Must().BeTrue();
        reporter.CanCancelOperation.Must().BeTrue();
    }

    [Fact]
    public void Begin_with_unknown_total_marks_indeterminate()
    {
        var reporter = new FileOperationReporter();

        reporter.Begin(FileOperationKind.Delete, totalItems: 0, title: "Deleting");

        reporter.IsIndeterminate.Must().BeTrue();
    }

    [Fact]
    public void Report_updates_progress_fraction_and_detail()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Copy, 3, "Copying items");

        reporter.Report(new FileOperationProgress(1, 3, @"C:\Temp\file.txt", FileOperationKind.Copy));

        reporter.Progress.Must().BeApproximately(1.0 / 3.0, 1e-5);
        reporter.IsIndeterminate.Must().BeFalse();
        reporter.ActiveTitle.Must().Contain("1 of 3");
        reporter.ActiveDetail.Must().Be("file.txt");
    }

    [Fact]
    public void Complete_clears_active_and_emits_succeeded_entry()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Copy, 1, "Copying items");

        reporter.Complete(FileOperationKind.Copy, itemCount: 1, message: "Copied 1 item");

        reporter.HasActive.Must().BeFalse();
        reporter.Progress.Must().Be(1);
        reporter.HasCompleted.Must().BeTrue();

        reporter.Completed.Must().HaveCount(1);
        var entry = reporter.Completed[0];
        entry.Message.Must().Be("Copied 1 item");
        entry.Succeeded.Must().BeTrue();
        entry.Failed.Must().BeFalse();
        entry.Cancelled.Must().BeFalse();
        entry.Kind.Must().Be(FileOperationKind.Copy);
        entry.ItemCount.Must().Be(1);
    }

    [Fact]
    public void Fail_emits_failure_entry()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Delete, 1, "Deleting");

        reporter.Fail("Delete failed");

        reporter.HasActive.Must().BeFalse();
        reporter.Completed.Must().HaveCount(1);
        var entry = reporter.Completed[0];
        entry.Failed.Must().BeTrue();
        entry.Succeeded.Must().BeFalse();
        entry.Cancelled.Must().BeFalse();
        entry.Kind.Must().BeNull();
        entry.ItemCount.Must().Be(0);
    }

    [Fact]
    public void Pause_and_resume_update_operation_control_state()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Copy, 2, "Copying");

        reporter.PauseOperationCommand.Execute(null);

        reporter.IsPaused.Must().BeTrue();
        reporter.CanPauseOperation.Must().BeFalse();
        reporter.CanResumeOperation.Must().BeTrue();

        reporter.ResumeOperationCommand.Execute(null);

        reporter.IsPaused.Must().BeFalse();
        reporter.CanPauseOperation.Must().BeTrue();
        reporter.CanResumeOperation.Must().BeFalse();
    }

    [Fact]
    public void Cancel_operation_signals_token()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Move, 1, "Moving");

        reporter.CancelOperationCommand.Execute(null);

        reporter.CancellationToken.IsCancellationRequested.Must().BeTrue();
        reporter.IsPaused.Must().BeFalse();
    }

    [Fact]
    public void Cancelled_emits_neutral_completed_entry()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Copy, 1, "Copying");

        reporter.Cancelled("Operation cancelled");

        reporter.HasActive.Must().BeFalse();
        reporter.Completed.Must().HaveCount(1);
        var entry = reporter.Completed[0];
        entry.Cancelled.Must().BeTrue();
        entry.Succeeded.Must().BeFalse();
        entry.Failed.Must().BeFalse();
    }

    [Fact]
    public void ClearCompleted_removes_entries_and_updates_has_completed()
    {
        var reporter = new FileOperationReporter();
        reporter.Complete(FileOperationKind.Copy, 1, "Copied");
        reporter.HasCompleted.Must().BeTrue();

        reporter.ClearCompletedCommand.Execute(null);

        reporter.HasCompleted.Must().BeFalse();
        reporter.Completed.Must().BeEmpty();
    }

    [Fact]
    public void Report_when_not_active_is_ignored()
    {
        var reporter = new FileOperationReporter();

        reporter.Report(new FileOperationProgress(1, 4, "path", FileOperationKind.Copy));

        reporter.HasActive.Must().BeFalse();
        reporter.Progress.Must().Be(0);
    }
}
