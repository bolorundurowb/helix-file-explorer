using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// Runs asynchronous work safely in the background, logging any unexpected failures.
/// This is intended for opportunistic UI refreshes (sidebar icons, network discovery,
/// file-system watcher reactions) where awaiting the task is unnecessary but exceptions
/// and cancellation must still be observed consistently.
/// </summary>
public static class FireAndForgetSafe
{
    /// <summary>
    /// Fires the supplied async work without awaiting it. Operation-canceled exceptions
    /// are swallowed; everything else is logged.
    /// </summary>
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

    /// <summary>
    /// Fires the supplied task without awaiting it. Operation-canceled exceptions are
    /// swallowed; everything else is logged.
    /// </summary>
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
            // Expected for background work that observes cancellation; no action needed.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fire-and-forget task failed in {Caller} ({File})", caller, file);
        }
    }
}

/// <summary>Extension-method convenience for <see cref="FireAndForgetSafe"/>.</summary>
public static class FireAndForgetSafeExtensions
{
    /// <summary>Fires the task without awaiting it, logging any unexpected failure.</summary>
    public static void FireAndForget(
        this Task task,
        ILogger logger,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        FireAndForgetSafe.Run(task, logger, caller, file);
    }

    /// <summary>Fires the async work without awaiting it, logging any unexpected failure.</summary>
    public static void FireAndForget(
        this Func<Task> work,
        ILogger logger,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        FireAndForgetSafe.Run(work, logger, caller, file);
    }
}
