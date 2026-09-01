namespace HelixExplorer.Windows;

internal static class NativeCallTimeout
{
    public static async Task<T> AwaitAsync<T>(
        Task<T> nativeCall,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var delay = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(nativeCall, delay).ConfigureAwait(false);
        if (completed == nativeCall)
            return await nativeCall.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // Native WNet and shell calls cannot be interrupted safely. Stop making the caller wait,
        // while observing a later fault from the abandoned worker.
        _ = nativeCall.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        throw new TimeoutException($"The native call exceeded its {timeout.TotalSeconds:0.#} second budget.");
    }
}
