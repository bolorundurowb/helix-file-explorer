using System.Text.Json.Serialization;

namespace HelixExplorer.Core.Theming;

[JsonConverter(typeof(UiFontFamilyJsonConverter))]
public enum UiFontFamily
{
    System,
    Inter,
    SourceSans3,
    IbmPlexSans,
    GeistSans
}
