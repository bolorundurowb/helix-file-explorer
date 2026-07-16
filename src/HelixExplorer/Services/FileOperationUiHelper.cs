using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public static class FileOperationUiHelper
{
    public static IFileConflictResolver CreateConflictResolver(IUserDialogService dialogs)
        => new FileConflictResolver(dialogs);

    public static async Task ReportResultAsync(
        IUserDialogService dialogs,
        FileOperationResult result,
        string operationName,
        Action<string> setStatusText)
    {
        if (result.Failed > 0)
        {
            var first = result.Failures[0];
            await dialogs.ShowErrorAsync(
                $"{operationName} failed",
                $"{Path.GetFileName(first.Path)}: {first.Message}").ConfigureAwait(true);
        }

        if (result.HasIssues)
            await dialogs.ShowOperationSummaryAsync(result, operationName).ConfigureAwait(true);

        if (result.Succeeded > 0 && !result.HasIssues)
            setStatusText($"{operationName} completed ({result.Succeeded} item(s))");
        else if (result.Failed > 0 && result.Succeeded == 0)
            setStatusText($"{operationName} failed");
    }
}
