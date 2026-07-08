namespace HelixExplorer.Core.FileSystem;

public static class ClipboardCutState
{
    public static bool IsPathCut(IClipboardService clipboard, string path)
    {
        if (clipboard.Current is not { Operation: ClipboardOperation.Cut } payload)
            return false;

        var normalized = Normalize(path);
        foreach (var cutPath in payload.Paths)
        {
            if (string.Equals(Normalize(cutPath), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string Normalize(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
