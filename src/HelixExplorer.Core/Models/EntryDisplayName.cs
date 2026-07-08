namespace HelixExplorer.Core.Models;

public static class EntryDisplayName
{
    public static string Format(in FileSystemEntry entry, bool showFileExtensions)
    {
        if (entry.IsDirectory || showFileExtensions || string.IsNullOrEmpty(entry.Extension))
            return entry.Name;

        var stem = Path.GetFileNameWithoutExtension(entry.Name);
        return string.IsNullOrEmpty(stem) ? entry.Name : stem;
    }
}
