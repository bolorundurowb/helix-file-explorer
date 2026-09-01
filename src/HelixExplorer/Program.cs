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
            // The collection root is scanned recursively, so every family subfolder under
            // Assets/Fonts registers under the single "fonts:Helix" key (UiFontCatalog resolves
            // each family as "fonts:Helix#<Family Name>").
            .ConfigureFonts(fonts => fonts.AddFontCollection(new EmbeddedFontCollection(
                new Uri("fonts:Helix", UriKind.Absolute),
                new Uri("avares://HelixExplorer/Assets/Fonts", UriKind.Absolute))))
            .LogToTrace();
}
