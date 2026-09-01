namespace HelixExplorer.ViewModels.Pane;

public sealed class PaneArchiveCommands
{
    public string GetUniqueArchivePath(string path)
        => PaneFileOperationCoordinator.GetUniquePath(path);

    public string GetUniqueExtractionDirectory(string path)
        => PaneFileOperationCoordinator.GetUniqueDirectory(path);

    public HashSet<string> SnapshotTopLevelNames(string directory)
    {
        try
        {
            return new HashSet<string>(
                Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .OfType<string>(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
