using System.Text.Json;

namespace HelixExplorer.Services;

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
}

public sealed record UpdateCheckResult(
    bool Succeeded,
    bool HasUpdate,
    string Status,
    string? ReleaseUrl = null);

public sealed class GitHubUpdateChecker(HttpClient httpClient) : IUpdateChecker
{
    private const string ReleasesUrl =
        "https://api.github.com/repos/bolorundurowb/helix-file-explorer/releases/latest";

    public async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(ReleasesUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Failed();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var tagName = document.RootElement.GetProperty("tag_name").GetString();
            var releaseUrl = document.RootElement.GetProperty("html_url").GetString();
            if (string.IsNullOrEmpty(tagName))
                return new(true, false, "No release information found.");

            var latestVersion = tagName.TrimStart('v', 'V');
            if (IsNewerVersion(latestVersion, currentVersion))
            {
                return new(
                    true,
                    true,
                    $"Update available: v{latestVersion} (current: v{currentVersion})",
                    releaseUrl);
            }

            return new(true, false, $"You are up to date (v{currentVersion})");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed();
        }
    }

    internal static bool IsNewerVersion(string latest, string current)
        => Version.TryParse(latest, out var latestVersion)
           && Version.TryParse(current, out var currentVersion)
           && latestVersion > currentVersion;

    private static UpdateCheckResult Failed()
        => new(false, false, "Could not check for updates. Try again later.");
}
