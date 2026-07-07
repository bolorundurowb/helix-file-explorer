using Avalonia;
using System;

namespace HelixExplorer;

internal sealed class Program
{
    // Initialization code. Don't use any of Avalonia's visual types here — they're not
    // initialized before the framework starts up.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}