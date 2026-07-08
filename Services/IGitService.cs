using HelixExplorer.Models;

namespace HelixExplorer.Services;

/// <summary>Reads lightweight git repository state for a directory.</summary>
public interface IGitService
{
    /// <summary>Returns the git status of <paramref name="path"/> or its nearest enclosing repository.</summary>
    ValueTask<GitStatus> GetStatusAsync(string path, CancellationToken token = default);

    /// <summary>Returns true when <paramref name="path"/> lives inside a git working tree.</summary>
    bool IsInsideRepository(string path);

    /// <summary>Lists local branch names for the repository enclosing <paramref name="path"/>.</summary>
    ValueTask<IReadOnlyList<string>> ListBranchesAsync(string path, CancellationToken token = default);

    /// <summary>Checks out <paramref name="branch"/> in the repository enclosing <paramref name="path"/>. Returns true on success.</summary>
    ValueTask<bool> CheckoutBranchAsync(string path, string branch, CancellationToken token = default);
}
