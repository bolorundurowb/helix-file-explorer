using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public sealed class ElevatedDeleteResultTests
{
    private const int EUnexpected = unchecked((int)0x8000FFFF);
    private const int ErrorCancelled = unchecked((int)0x800704C7);
    private const int ErrorElevationRequired = unchecked((int)0x800702E4);
    private const int ErrorAccessDenied = unchecked((int)0x80070005);

    [Fact]
    public void WithElevation_ClearsErrorUiSoTheShellCanPrompt()
    {
        var elevated = DeletePromptPolicy.Silent.WithElevation(recycle: false);

        elevated.SuppressErrorUi.Must().BeFalse();
        elevated.ShowElevationPrompt.Must().BeTrue();
        elevated.RecycleOnDelete.Must().BeFalse();
    }

    [Fact]
    public void WithElevation_RecycleStillClearsErrorUi()
    {
        var elevated = DeletePromptPolicy.Silent.WithElevation(recycle: true);

        elevated.SuppressErrorUi.Must().BeFalse();
        elevated.ShowElevationPrompt.Must().BeTrue();
        elevated.RecycleOnDelete.Must().BeTrue();
    }

    [Fact]
    public void Silent_KeepsErrorUiSuppressed()
    {
        DeletePromptPolicy.Silent.SuppressErrorUi.Must().BeTrue();
        DeletePromptPolicy.Silent.ShowElevationPrompt.Must().BeFalse();
    }

    [Theory]
    [InlineData(EUnexpected)]
    [InlineData(ErrorCancelled)]
    [InlineData(ErrorElevationRequired)]
    [InlineData(ErrorAccessDenied)]
    public void MapDeleteResults_ElevationFailureUsesPermissionTextAndKeepsDeletedItems(int hresult)
    {
        var requested = new[] { @"C:\gone.txt", @"C:\protected.txt", @"C:\also-gone.txt" };
        var deleted = new[] { @"c:\gone.txt", @"C:\also-gone.txt" };

        var (succeeded, failures) = FileSystemErrorMessages.MapDeleteResults(requested, deleted, hresult);

        succeeded.Must().Be(2);
        failures.Must().HaveCount(1);
        failures[0].Path.Must().Be(@"C:\protected.txt");
        failures[0].Message.Must().Be(FileSystemErrorMessages.AdministratorPermissionRequired);
        failures[0].Message.Must().NotContain("Catastrophic");
        failures[0].Message.Must().NotContain("0x8000FFFF");
    }

    [Fact]
    public void MapElevationResults_OneProtectedItemDoesNotFailItemsThatDeleted()
    {
        var requested = new[] { @"D:\a.txt", @"D:\locked.txt" };

        var (succeeded, failures) = FileSystemErrorMessages.MapElevationResults(
            requested,
            deleted: [@"D:\a.txt"]);

        succeeded.Must().Be(1);
        failures.Must().HaveCount(1);
        failures[0].Path.Must().Be(@"D:\locked.txt");
        failures[0].Message.Must().Be(FileSystemErrorMessages.AdministratorPermissionRequired);
    }

    [Fact]
    public void MapDeleteResults_OtherShellFailureDoesNotSurfaceTheHresultText()
    {
        var (succeeded, failures) = FileSystemErrorMessages.MapDeleteResults(
            [@"C:\busy.txt", @"C:\ok.txt"],
            deleted: [@"C:\ok.txt"],
            failureHResult: unchecked((int)0x80070020));

        succeeded.Must().Be(1);
        failures.Must().HaveCount(1);
        failures[0].Message.Must().Be("The file operation failed");
        failures[0].Message.Must().NotContain("Catastrophic");
    }

    [Fact]
    public void MapDeleteResults_CompletedBatchKeepsGenericRecycleMessage()
    {
        var (succeeded, failures) = FileSystemErrorMessages.MapDeleteResults(
            [@"C:\ok.txt", @"C:\skipped.txt"],
            deleted: [@"C:\ok.txt"],
            failureHResult: null);

        succeeded.Must().Be(1);
        failures[0].Message.Must().Be(FileSystemErrorMessages.RecycleBinOperationFailed);
    }
}
