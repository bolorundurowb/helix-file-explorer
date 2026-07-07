using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>Reads lightweight git repository state for a directory.</summary>
public interface IGitService
{
    /// <summary>Returns the git status of <paramref name="path"/> or its nearest enclosing repository.</summary>
    ValueTask<GitStatus> GetStatusAsync(string path, CancellationToken token = default);

    /// <summary>Returns true when <paramref name="path"/> lives inside a git working tree.</summary>
    bool IsInsideRepository(string path);
}