using Avalonia;
using Avalonia.Headless;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// Entry point for the headless session. <see cref="HeadlessUnitTestSession.StartNew"/> discovers
/// <c>BuildAvaloniaApp</c> here, boots Avalonia headlessly with the real <see cref="HelixExplorer.App"/>
/// so its resources/styles load, and runs a dispatcher loop on a background thread. The desktop-only
/// startup in <c>OnFrameworkInitializationCompleted</c> is skipped because there is no desktop lifetime.
/// </summary>
public static class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HelixExplorer.App>();
}

/// <summary>
/// Marshals UI work onto the headless dispatcher thread. xUnit runs tests on thread-pool threads, so
/// callers wrap window/view construction in <see cref="RunOnUiThread(Action)"/>.
/// </summary>
public static class HeadlessSession
{
    private static readonly object Gate = new();
    private static HeadlessUnitTestSession? _session;

    private static HeadlessUnitTestSession Session
    {
        get
        {
            if (_session is null)
            {
                lock (Gate)
                {
                    _session ??= HeadlessUnitTestSession.StartNew(typeof(HeadlessAppBuilder));
                }
            }

            return _session;
        }
    }

    public static void RunOnUiThread(Action action)
        => Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();

    public static T RunOnUiThread<T>(Func<T> action)
        => Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
}
