namespace HelixExplorer.ViewModels.Pane;

public interface IPaneInlineRenameHost
{
    IReadOnlyList<EntryItemViewModel> SelectedEntries { get; }
    bool IsRenaming { get; set; }
    string RenameText { get; set; }
    string StatusText { get; set; }

    Task RefreshAfterRenameAsync(string oldPath);
}

public sealed class PaneInlineRenameCoordinator(PaneFileOperationCoordinator fileOperations)
{
    private EntryItemViewModel? _entry;
    private bool _isCommitting;

    public void Begin(IPaneInlineRenameHost host)
    {
        if (host.SelectedEntries.Count != 1)
            return;

        var entry = host.SelectedEntries[0];
        Clear(host);
        _entry = entry;
        entry.RenameText = entry.Name;
        host.RenameText = entry.Name;
        entry.IsRenaming = true;
        host.IsRenaming = true;
    }

    public async Task CommitAsync(IPaneInlineRenameHost host)
    {
        if (_isCommitting)
            return;

        if (!host.IsRenaming || _entry is null)
        {
            Clear(host);
            return;
        }

        var entry = _entry;
        var newName = entry.RenameText.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name)
        {
            Clear(host);
            return;
        }

        _isCommitting = true;
        try
        {
            var oldPath = entry.FullPath;
            await fileOperations.RenameAsync(
                oldPath,
                newName,
                refreshAsync: () => host.RefreshAfterRenameAsync(oldPath),
                onClearRename: () => Clear(host),
                setStatusText: text => host.StatusText = text).ConfigureAwait(true);
        }
        finally
        {
            _isCommitting = false;
        }
    }

    public void Clear(IPaneInlineRenameHost host)
    {
        // Clearing the host before the entry prevents LostFocus from re-entering commit with stale state.
        var entry = _entry;
        _entry = null;
        host.IsRenaming = false;
        host.RenameText = string.Empty;

        if (entry is not null)
        {
            entry.IsRenaming = false;
            entry.RenameText = string.Empty;
        }
    }

    public static int GetBaseNameLength(string name, bool isDirectory)
    {
        if (string.IsNullOrEmpty(name) || isDirectory)
            return name.Length;

        var extension = Path.GetExtension(name);
        return string.IsNullOrEmpty(extension) || extension.Length >= name.Length
            ? name.Length
            : name.Length - extension.Length;
    }
}
