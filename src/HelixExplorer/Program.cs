using Avalonia;
using Avalonia.Media.Fonts;
using HelixExplorer.Core.Logging;

namespace HelixExplorer;

internal static class Program
{
    public static string[] StartupArgs { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        StartupArgs = args;
        CrashLogger.InstallEarly();

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLogger.Report("Main", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFontCollection(new EmbeddedFontCollection(
                    new Uri("fonts:HelixSourceSans", UriKind.Absolute),
                    new Uri("avares://HelixExplorer/Assets/Fonts/SourceSans3", UriKind.Absolute)));
                fonts.AddFontCollection(new EmbeddedFontCollection(
                    new Uri("fonts:HelixPlex", UriKind.Absolute),
                    new Uri("avares://HelixExplorer/Assets/Fonts/IBMPlexSans", UriKind.Absolute)));
                fonts.AddFontCollection(new EmbeddedFontCollection(
                    new Uri("fonts:HelixGeist", UriKind.Absolute),
                    new Uri("avares://HelixExplorer/Assets/Fonts/GeistSans", UriKind.Absolute)));
            })
            .LogToTrace();
}
