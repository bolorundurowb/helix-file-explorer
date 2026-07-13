using Avalonia;
using Avalonia.Media.Fonts;

namespace HelixExplorer;

internal static class Program
{
    public static string[] StartupArgs { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        StartupArgs = args;
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .ConfigureFonts(fonts => fonts.AddFontCollection(new EmbeddedFontCollection(
                new Uri("fonts:Helix", UriKind.Absolute),
                new Uri("avares://HelixExplorer/Assets/Fonts/DMSans", UriKind.Absolute))))
            .LogToTrace();
}
