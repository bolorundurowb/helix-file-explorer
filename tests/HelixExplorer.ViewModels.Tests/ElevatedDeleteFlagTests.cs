using HelixExplorer.Core.FileSystem;
using HelixExplorer.Windows.FileSystem;
using Vanara.Windows.Shell;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// The shell flags are chosen without performing a delete. NoErrorUI has to be cleared or UAC never appears.
/// </summary>
public sealed class ElevatedDeleteFlagTests
{
    [Fact]
    public void ElevatedPermanentDelete_ClearsNoErrorUiAndRequestsPrompt()
    {
        var flags = ShellFileOperationsHelper.FlagsFor(DeletePromptPolicy.Silent.WithElevation(recycle: false));

        flags.HasFlag(ShellFileOperations.OperationFlags.NoErrorUI).Must().BeFalse();
        flags.HasFlag(ShellFileOperations.OperationFlags.ShowElevationPrompt).Must().BeTrue();
        flags.HasFlag(ShellFileOperations.OperationFlags.RecycleOnDelete).Must().BeFalse();
        flags.HasFlag(ShellFileOperations.OperationFlags.Silent).Must().BeTrue();
        flags.HasFlag(ShellFileOperations.OperationFlags.NoConfirmation).Must().BeTrue();
    }

    [Fact]
    public void ElevatedRecycleDelete_ClearsNoErrorUiAndRecycles()
    {
        var flags = ShellFileOperationsHelper.FlagsFor(DeletePromptPolicy.Silent.WithElevation(recycle: true));

        flags.HasFlag(ShellFileOperations.OperationFlags.NoErrorUI).Must().BeFalse();
        flags.HasFlag(ShellFileOperations.OperationFlags.ShowElevationPrompt).Must().BeTrue();
        flags.HasFlag(ShellFileOperations.OperationFlags.RecycleOnDelete).Must().BeTrue();
    }

    [Fact]
    public void SilentDelete_KeepsNoErrorUi()
    {
        var flags = ShellFileOperationsHelper.FlagsFor(DeletePromptPolicy.Silent);

        flags.HasFlag(ShellFileOperations.OperationFlags.NoErrorUI).Must().BeTrue();
        flags.HasFlag(ShellFileOperations.OperationFlags.ShowElevationPrompt).Must().BeFalse();
    }
}
