namespace HelixExplorer.Core.Git;

public interface IGitProvider
{
    bool IsInsideRepository(string path);

    ValueTask<GitStatusSnapshot> GetStatusAsync(string path, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<string>> ListBranchesAsync(string path, CancellationToken cancellationToken = default);

    ValueTask<bool> CheckoutBranchAsync(string path, string branch, CancellationToken cancellationToken = default);
}
