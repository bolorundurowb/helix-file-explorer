using HelixExplorer.Core.FileSystem;
using HelixExplorer.Services;
using Xunit;

namespace HelixExplorer.ViewModels.Tests;

public class FileOperationReporterTests
{
    [Fact]
    public void Begin_resets_progress_and_marks_active()
    {
        var reporter = new FileOperationReporter();

        reporter.Begin(FileOperationKind.Copy, totalItems: 4, title: "Copying items");

        Assert.True(reporter.HasActive);
        Assert.True(reporter.IsBusy);
        Assert.Equal(0, reporter.Progress);
        Assert.False(reporter.IsIndeterminate);
        Assert.Equal("Copying items", reporter.ActiveTitle);
        Assert.True(reporter.CanPauseOperation);
        Assert.True(reporter.CanCancelOperation);
    }

    [Fact]
    public void Begin_with_unknown_total_marks_indeterminate()
    {
        var reporter = new FileOperationReporter();

        reporter.Begin(FileOperationKind.Delete, totalItems: 0, title: "Deleting");

        Assert.True(reporter.IsIndeterminate);
    }

    [Fact]
    public void Report_updates_progress_fraction_and_detail()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Copy, 3, "Copying items");

        reporter.Report(new FileOperationProgress(1, 3, @"C:\Temp\file.txt", FileOperationKind.Copy));

        Assert.Equal(1.0 / 3.0, reporter.Progress, 5);
        Assert.False(reporter.IsIndeterminate);
        Assert.Contains("1 of 3", reporter.ActiveTitle);
        Assert.Equal("file.txt", reporter.ActiveDetail);
    }

    [Fact]
    public void Complete_clears_active_and_emits_succeeded_entry()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Copy, 1, "Copying items");

        reporter.Complete(FileOperationKind.Copy, itemCount: 1, message: "Copied 1 item");

        Assert.False(reporter.HasActive);
        Assert.Equal(1, reporter.Progress);
        Assert.True(reporter.HasCompleted);

        var entry = Assert.Single(reporter.Completed);
        Assert.Equal("Copied 1 item", entry.Message);
        Assert.True(entry.Succeeded);
        Assert.False(entry.Failed);
        Assert.False(entry.Cancelled);
        Assert.Equal(FileOperationKind.Copy, entry.Kind);
        Assert.Equal(1, entry.ItemCount);
    }

    [Fact]
    public void Fail_emits_failure_entry()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Delete, 1, "Deleting");

        reporter.Fail("Delete failed");

        Assert.False(reporter.HasActive);
        var entry = Assert.Single(reporter.Completed);
        Assert.True(entry.Failed);
        Assert.False(entry.Succeeded);
        Assert.False(entry.Cancelled);
        Assert.Null(entry.Kind);
        Assert.Equal(0, entry.ItemCount);
    }

    [Fact]
    public void Pause_and_resume_update_operation_control_state()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Copy, 2, "Copying");

        reporter.PauseOperationCommand.Execute(null);

        Assert.True(reporter.IsPaused);
        Assert.False(reporter.CanPauseOperation);
        Assert.True(reporter.CanResumeOperation);

        reporter.ResumeOperationCommand.Execute(null);

        Assert.False(reporter.IsPaused);
        Assert.True(reporter.CanPauseOperation);
        Assert.False(reporter.CanResumeOperation);
    }

    [Fact]
    public void Cancel_operation_signals_token()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Move, 1, "Moving");

        reporter.CancelOperationCommand.Execute(null);

        Assert.True(reporter.CancellationToken.IsCancellationRequested);
        Assert.False(reporter.IsPaused);
    }

    [Fact]
    public void Cancelled_emits_neutral_completed_entry()
    {
        var reporter = new FileOperationReporter();
        reporter.Begin(FileOperationKind.Copy, 1, "Copying");

        reporter.Cancelled("Operation cancelled");

        Assert.False(reporter.HasActive);
        var entry = Assert.Single(reporter.Completed);
        Assert.True(entry.Cancelled);
        Assert.False(entry.Succeeded);
        Assert.False(entry.Failed);
    }

    [Fact]
    public void ClearCompleted_removes_entries_and_updates_has_completed()
    {
        var reporter = new FileOperationReporter();
        reporter.Complete(FileOperationKind.Copy, 1, "Copied");
        Assert.True(reporter.HasCompleted);

        reporter.ClearCompletedCommand.Execute(null);

        Assert.False(reporter.HasCompleted);
        Assert.Empty(reporter.Completed);
    }

    [Fact]
    public void Report_when_not_active_is_ignored()
    {
        var reporter = new FileOperationReporter();

        reporter.Report(new FileOperationProgress(1, 4, "path", FileOperationKind.Copy));

        Assert.False(reporter.HasActive);
        Assert.Equal(0, reporter.Progress);
    }
}