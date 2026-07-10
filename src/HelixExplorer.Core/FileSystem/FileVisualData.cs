namespace HelixExplorer.Core.FileSystem;

public sealed class FileVisualData(byte[] png)
{
    public byte[] Png { get; } = png;
}

public sealed class FileVisualRequest(string path, bool isDirectory, int size, bool preferThumbnail)
{
    public string Path { get; } = path;
    public bool IsDirectory { get; } = isDirectory;
    public int Size { get; } = size;
    public bool PreferThumbnail { get; } = preferThumbnail;
}
