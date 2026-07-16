namespace HelixExplorer.Core.FileSystem;

public static class ShellPath
{
    public const string RecycleBin = "shell:RecycleBinFolder";
    public const string Network = "shell:NetworkPlacesFolder";

    public static bool IsShellPath(string? path)
        => !string.IsNullOrEmpty(path)
           && path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    public static bool IsRecycleBin(string? path)
        => string.Equals(path, RecycleBin, StringComparison.OrdinalIgnoreCase);
}
