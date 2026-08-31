using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// Opportunistic background work (sidebar icons, network discovery, watcher reactions)
/// where awaiting is unnecessary but exceptions and cancellation must still be observed.
/// </summary>
public static class FireAndForgetSafe
{
    public static void Run(
        Func<Task> work,
        ILogger logger,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(logger);

        _ = RunCoreAsync(work, logger, caller, file);
    }

    public static void Run(
        Task task,
        ILogger logger,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(logger);

        _ = RunCoreAsync(() => task, logger, caller, file);
    }

    private static async Task RunCoreAsync(
        Func<Task> work,
        ILogger logger,
        string caller,
        string file)
    {
        try
        {
            var task = work();
            if (task is null)
            {
                logger.LogError(
                    "FireAndForgetSafe: work delegate returned null instead of a Task in {Caller} ({File}).",
                    caller,
                    file);
                return;
            }

            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        // Deliberately broad: this IS the fire-and-forget safety net - by construction there is no
        // caller left to observe an exception from `work()` by the time it's thrown, so catching and
        // logging every possible exception type here (rather than a chosen subset) is the entire
        // point of this helper, not a shortcut around it.
#pragma warning disable CA1031
        catch (Exception ex)
        {
            logger.LogError(ex, "Fire-and-forget task failed in {Caller} ({File})", caller, file);
        }
#pragma warning restore CA1031
    }
}

public static class FireAndForgetSafeExtensions
{
    public static void FireAndForget(
        this Task task,
        ILogger logger,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        FireAndForgetSafe.Run(task, logger, caller, file);
    }

    public static void FireAndForget(
        this Func<Task> work,
        ILogger logger,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        FireAndForgetSafe.Run(work, logger, caller, file);
    }
}
