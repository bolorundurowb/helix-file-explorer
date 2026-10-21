namespace HelixExplorer.Core.Theming;

public static class UiFontCatalog
{
    public const string InterCollectionScheme = "fonts:Inter#Inter";
    public const string SourceSans3CollectionScheme = "fonts:HelixSourceSans#Source Sans 3";
    public const string IbmPlexSansCollectionScheme = "fonts:HelixPlex#IBM Plex Sans";
    public const string GeistSansCollectionScheme = "fonts:HelixGeist#Geist";

    public static IReadOnlyList<UiFontOption> Options { get; } =
    [
        new(UiFontFamily.System, "System default"),
        new(UiFontFamily.Inter, "Inter"),
        new(UiFontFamily.SourceSans3, "Source Sans 3"),
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
            UiFontFamily.SourceSans3 => SourceSans3CollectionScheme,
            UiFontFamily.IbmPlexSans => IbmPlexSansCollectionScheme,
            UiFontFamily.GeistSans => GeistSansCollectionScheme,
            _ => GetSystemFontFamilySource()
        };
}
