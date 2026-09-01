namespace HelixExplorer.ViewModels.Pane;

public sealed class PaneRenameController
{
    public EntryItemViewModel? Entry { get; private set; }

    public bool IsCommitting { get; set; }

    public bool Begin(IReadOnlyList<EntryItemViewModel> selection)
    {
        Clear();
        if (selection.Count != 1)
            return false;

        Entry = selection[0];
        Entry.RenameText = Entry.Name;
        Entry.IsRenaming = true;
        return true;
    }

    public void Clear()
    {
        var entry = Entry;
        Entry = null;
        if (entry is null)
            return;

        entry.IsRenaming = false;
        entry.RenameText = string.Empty;
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
