using System.Diagnostics;

namespace HelixExplorer.Core.Infrastructure;

public readonly record struct ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan hardTimeout,
        bool killOnCancellation,
        CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan hardTimeout,
        bool killOnCancellation,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
        if (!process.Start())
            return new ProcessRunResult(-1, string.Empty, string.Empty);

        try
        {
            process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
            // The process exited before stdin could be closed.
        }

        using var timeout = new CancellationTokenSource(hardTimeout);
        using var timeoutRegistration = timeout.Token.Register(static state => TryKill((Process)state!), process);
        using var cancellationRegistration = killOnCancellation
            ? cancellationToken.Register(static state => TryKill((Process)state!), process)
            : default;

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var waitToken = killOnCancellation ? cancellationToken : CancellationToken.None;
        await process.WaitForExitAsync(waitToken).ConfigureAwait(false);
        var standardOutput = await stdoutTask.ConfigureAwait(false);
        var standardError = await stderrTask.ConfigureAwait(false);
        return new ProcessRunResult(process.ExitCode, standardOutput, standardError);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
#pragma warning disable CA1031
        catch
        {
            // Exit and cancellation callbacks race normally.
        }
#pragma warning restore CA1031
    }
}
