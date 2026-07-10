namespace HelixExplorer.Core.FileSystem;

/// <summary>Helpers for Windows shell namespace paths (e.g. Recycle Bin).</summary>
public static class ShellPath
{
    public const string RecycleBin = "shell:RecycleBinFolder";

    public static bool IsShellPath(string? path)
        => !string.IsNullOrEmpty(path)
           && path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    public static bool IsRecycleBin(string? path)
        => string.Equals(path, RecycleBin, StringComparison.OrdinalIgnoreCase);
}
