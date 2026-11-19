namespace HelixExplorer.Core.Theming;

public static class UiFontCatalog
{
    public const string InterCollectionScheme = "fonts:Inter#Inter";
    public const string FigtreeCollectionScheme = "fonts:HelixFigtree#Figtree";
    public const string IbmPlexSansCollectionScheme = "fonts:HelixPlex#IBM Plex Sans";
    public const string GeistSansCollectionScheme = "fonts:HelixGeist#Geist";

    public static IReadOnlyList<UiFontOption> Options { get; } =
    [
        new(UiFontFamily.System, "System default"),
        new(UiFontFamily.Inter, "Inter"),
        new(UiFontFamily.Figtree, "Figtree"),
        new(UiFontFamily.IbmPlexSans, "IBM Plex Sans"),
        new(UiFontFamily.GeistSans, "Geist Sans")
    ];

    public static string GetDisplayName(UiFontFamily font) =>
        Options.FirstOrDefault(option => option.Value == font)?.Label ?? font.ToString();

    public static string GetSystemFontFamilySource()
    {
        if (OperatingSystem.IsWindows())
            return "Segoe UI Variable Text, Segoe UI";

        if (OperatingSystem.IsMacOS())
            return ".AppleSystemUIFont, SF Pro Text, Helvetica Neue";

        return "Cantarell, Ubuntu, Noto Sans, fonts:Inter#Inter";
    }

    public static string ResolveFontFamilySource(UiFontFamily font) =>
        font switch
        {
            UiFontFamily.Inter => InterCollectionScheme,
            UiFontFamily.Figtree => FigtreeCollectionScheme,
            UiFontFamily.IbmPlexSans => IbmPlexSansCollectionScheme,
            UiFontFamily.GeistSans => GeistSansCollectionScheme,
            _ => GetSystemFontFamilySource()
        };
}
