namespace HelixExplorer.Core.FileSystem;

public interface IShellContextMenuService
{
    ValueTask ShowMoreOptionsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}
