using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Localization;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Shell-facing pane actions (properties, context menu, terminal) kept off <see cref="PaneViewModel"/>.
/// </summary>
public sealed class PaneShellActionCoordinator(
    IShellContextMenuService shellContextMenu,
    ITerminalLauncher terminalLauncher,
    ILogger<PaneShellActionCoordinator> logger)
{
    public async Task ShowPropertiesAsync(IReadOnlyList<string> paths, nint ownerHandle, Action<string> setStatus)
    {
        if (paths.Count == 0)
            return;

        try
        {
            await shellContextMenu.ShowPropertiesAsync(paths[0], ownerHandle).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ShowProperties failed");
            setStatus(UiStrings.ShowPropertiesFailed);
        }
    }

    public async Task ShowMoreOptionsAsync(
        IReadOnlyList<string> paths,
        string folderPath,
        int x,
        int y,
        nint ownerHandle,
        Action<string> setStatus)
    {
        try
        {
            await shellContextMenu.ShowMoreOptionsAsync(
                folderPath,
                paths,
                ownerHandle,
                x,
                y).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ShowMoreOptions failed");
            setStatus(UiStrings.ShowMoreOptionsFailed);
        }
    }

    public bool TryOpenInTerminal(string directoryPath, Action<string> setStatus)
    {
        try
        {
            if (terminalLauncher.TryOpenInDirectory(directoryPath))
                return true;

            setStatus(UiStrings.OpenInTerminalFailed);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "OpenInTerminal failed");
            setStatus(UiStrings.OpenInTerminalFailed);
            return false;
        }
    }
}
