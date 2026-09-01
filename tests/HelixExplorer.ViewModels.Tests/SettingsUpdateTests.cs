using HelixExplorer.Services;

namespace HelixExplorer.ViewModels.Tests;

public class SettingsUpdateTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_PublishesServiceResultWithoutPolling()
    {
        var checker = new StubUpdateChecker(
            new UpdateCheckResult(true, true, "Update available", "https://example.test/release"));
        var viewModel = new SettingsPageViewModel(checker);

        await viewModel.CheckForUpdatesAsync();

        checker.Calls.Must().Be(1);
        viewModel.IsCheckingForUpdates.Must().BeFalse();
        viewModel.HasUpdate.Must().BeTrue();
        viewModel.UpdateStatus.Must().Be("Update available");
    }

    private sealed class StubUpdateChecker(UpdateCheckResult result) : IUpdateChecker
    {
        public int Calls { get; private set; }

        public Task<UpdateCheckResult> CheckAsync(
            string currentVersion,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
