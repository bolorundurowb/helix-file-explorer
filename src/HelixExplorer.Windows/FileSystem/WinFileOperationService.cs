using System.Diagnostics;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Windows.FileSystem;

public sealed class WinFileOperationService : IFileOperationService
{
    public async ValueTask CopyAsync(
        IReadOnlyList<string> sources,
        string destination,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        await Task.Run(() => ProcessSources(sources, destination, FileOperationKind.Copy, progress, ct, CopyOne), ct)
            .ConfigureAwait(false);
    }

    public async ValueTask MoveAsync(
        IReadOnlyList<string> sources,
        string destination,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        await Task.Run(() => ProcessSources(sources, destination, FileOperationKind.Move, progress, ct, MoveOne), ct)
            .ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(IReadOnlyList<string> paths, bool permanently, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (permanently)
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                        else if (Directory.Exists(path))
                            Directory.Delete(path, recursive: true);
                    }
                    else
                    {
                        SendToRecycleBin(path);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Delete failed for '{path}': {ex.Message}");
                }
            }
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask RenameAsync(string path, string newName, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var parent = Path.GetDirectoryName(path) ?? string.Empty;
            var newPath = Path.Combine(parent, newName);

            if (File.Exists(path))
                File.Move(path, newPath);
            else if (Directory.Exists(path))
                Directory.Move(path, newPath);
        }, ct).ConfigureAwait(false);
    }

    public async ValueTask<string> CreateFolderAsync(string parentPath, string name, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(parentPath, name);
            fullPath = FileOperationPathHelper.EnsureUniqueDirectoryPath(fullPath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }, ct).ConfigureAwait(false);
    }

    private static void ProcessSources(
        IReadOnlyList<string> sources,
        string destination,
        FileOperationKind kind,
        IProgress<FileOperationProgress>? progress,
        CancellationToken ct,
        Action<string, string, CancellationToken> operation)
    {
        var total = sources.Count;
        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var source = sources[i];
            progress?.Report(new FileOperationProgress(i, total, source, kind));
            operation(source, destination, ct);
            progress?.Report(new FileOperationProgress(i + 1, total, source, kind));
        }
    }

    private static void CopyOne(string source, string destination, CancellationToken ct)
    {
        var destPath = Path.Combine(destination, Path.GetFileName(source));

        if (File.Exists(source))
        {
            destPath = FileOperationPathHelper.EnsureUniqueFilePath(destPath);
            File.Copy(source, destPath, overwrite: false);
        }
        else if (Directory.Exists(source))
        {
            destPath = FileOperationPathHelper.EnsureUniqueDirectoryPath(destPath);
            CopyDirectory(source, destPath, ct);
        }
    }

    private static void MoveOne(string source, string destination, CancellationToken ct)
    {
        var destPath = Path.Combine(destination, Path.GetFileName(source));

        if (File.Exists(source))
        {
            destPath = FileOperationPathHelper.EnsureUniqueFilePath(destPath);
            File.Move(source, destPath);
        }
        else if (Directory.Exists(source))
        {
            destPath = FileOperationPathHelper.EnsureUniqueDirectoryPath(destPath);
            Directory.Move(source, destPath);
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)), ct);
        }
    }

    private static void SendToRecycleBin(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return;
            }

            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.Exists)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Recycle bin failed for '{path}': {ex.Message}");
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
