using HelixExplorer.Windows.Shell;
using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Windows.Tests;

public class WindowsInfrastructureTests
{
    [Fact]
    public async Task STATask_ReusesSingleStaThread()
    {
        var first = await STATask.Run(
            () => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        var second = await STATask.Run(() => Environment.CurrentManagedThreadId);

        second.Must().Be(first.Item1);
        first.Item2.Must().Be(ApartmentState.STA);
    }

    [Fact]
    public async Task STATask_CancelledQueuedWorkDoesNotRun()
    {
        using var release = new ManualResetEventSlim();
        var blocker = STATask.Run(() => release.Wait(TimeSpan.FromSeconds(5)));
        using var cts = new CancellationTokenSource();
        var ran = false;
        var queued = STATask.Run(() => ran = true, cts.Token);

        cts.Cancel();
        await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        release.Set();
        await blocker;

        ran.Must().BeFalse();
    }

    [Fact]
    public async Task NativeCallTimeout_AbandonsBlockedCall()
    {
        var blocked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await Xunit.Assert.ThrowsAsync<TimeoutException>(
            () => NativeCallTimeout.AwaitAsync(
                blocked.Task,
                TimeSpan.FromMilliseconds(25),
                CancellationToken.None));

        blocked.SetResult(1);
    }

    [Fact]
    public async Task FileVisualProvider_GenericIconsUseExtensionIdentity()
    {
        var root = Directory.CreateTempSubdirectory("helix-icon-tests-").FullName;
        try
        {
            var first = Path.Combine(root, "one.uncommonextension");
            var second = Path.Combine(root, "two.uncommonextension");
            await File.WriteAllTextAsync(first, "one");
            await File.WriteAllTextAsync(second, "two");
            var provider = new WinFileVisualProvider();

            var firstVisual = await provider.GetAsync(
                new FileVisualRequest(first, isDirectory: false, size: 32, preferThumbnail: false),
                CancellationToken.None);
            var secondVisual = await provider.GetAsync(
                new FileVisualRequest(second, isDirectory: false, size: 32, preferThumbnail: false),
                CancellationToken.None);

            firstVisual.Must().NotBeNull();
            secondVisual.Must().NotBeNull();
            secondVisual!.Png.SequenceEqual(firstVisual!.Png).Must().BeTrue();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
