namespace HelixExplorer.Core.Search;

public static class TextFileClassifier
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".cs", ".fs", ".vb", ".py", ".rs", ".go", ".java", ".kt",
        ".c", ".h", ".cpp", ".hpp", ".cc", ".hh", ".js", ".jsx", ".ts", ".tsx", ".json",
        ".xml", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf", ".csv", ".log",
        ".html", ".htm", ".css", ".scss", ".less", ".svg", ".sql", ".sh", ".bash", ".ps1",
        ".bat", ".cmd", ".gitignore", ".editorconfig", ".props", ".targets", ".csproj",
        ".sln", ".axaml", ".xaml", ".resx", ".plist", ".env", ".dockerfile"
    };

    public const long DefaultMaxBytes = 1 * 1024 * 1024;

    public static bool IsLikelyTextExtension(string? extension)
        => !string.IsNullOrEmpty(extension) && TextExtensions.Contains(extension);

    public static bool LooksBinary(ReadOnlySpan<byte> sample)
    {
        var length = Math.Min(sample.Length, 8192);
        for (var i = 0; i < length; i++)
        {
            if (sample[i] == 0)
                return true;
        }

        return false;
    }
}
