namespace HelixExplorer.Core.FileSystem;

public static class FileOperationPathHelper
{
    public static string EnsureUniqueFilePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var counter = 1;

        while (File.Exists(Path.Combine(dir, $"{name} ({counter}){ext}")))
            counter++;

        return Path.Combine(dir, $"{name} ({counter}){ext}");
    }

    public static string EnsureUniqueDirectoryPath(string path)
    {
        if (!Directory.Exists(path))
            return path;

        var parent = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileName(path);
        var counter = 1;

        while (Directory.Exists(Path.Combine(parent, $"{name} ({counter})")))
            counter++;

        return Path.Combine(parent, $"{name} ({counter})");
    }
}
