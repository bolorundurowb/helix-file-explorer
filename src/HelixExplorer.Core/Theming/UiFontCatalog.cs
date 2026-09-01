namespace HelixExplorer.Core.Theming;

public static class UiFontCatalog
{
    public const string BundledCollectionScheme = "fonts:Helix#DM Sans";
    public const string InterCollectionScheme = "fonts:Inter#Inter";
    public const string IbmPlexSansCollectionScheme = "fonts:Helix#IBM Plex Sans";
    public const string PlusJakartaSansCollectionScheme = "fonts:Helix#Plus Jakarta Sans";

    public static IReadOnlyList<UiFontOption> Options { get; } =
    [
        new(UiFontFamily.System, "System default"),
        new(UiFontFamily.DmSans, "DM Sans"),
        new(UiFontFamily.Inter, "Inter"),
        new(UiFontFamily.IbmPlexSans, "IBM Plex Sans"),
        new(UiFontFamily.PlusJakartaSans, "Plus Jakarta Sans")
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
            UiFontFamily.DmSans => BundledCollectionScheme,
            UiFontFamily.Inter => InterCollectionScheme,
            UiFontFamily.IbmPlexSans => IbmPlexSansCollectionScheme,
            UiFontFamily.PlusJakartaSans => PlusJakartaSansCollectionScheme,
            _ => GetSystemFontFamilySource()
        };
}
