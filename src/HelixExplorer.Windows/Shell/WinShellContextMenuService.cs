using System.Diagnostics;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Windows.Shell;

public sealed class WinShellContextMenuService : IShellContextMenuService
{
    public ValueTask ShowMoreOptionsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
            return ValueTask.CompletedTask;

        cancellationToken.ThrowIfCancellationRequested();

        var target = paths[0];
        if (File.Exists(target))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,{Quote(target)}",
                UseShellExecute = true
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        return ValueTask.CompletedTask;
    }

    private static string Quote(string value) => $"\"{value}\"";
}
