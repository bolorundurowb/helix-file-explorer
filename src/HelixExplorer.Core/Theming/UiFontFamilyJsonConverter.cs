using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelixExplorer.Core.Theming;

/// <summary>
/// Keeps a font name this build no longer ships from failing the whole settings document.
/// <see cref="Settings.JsonSettingsStore"/> falls back to a blank document when deserialization
/// throws, so an unknown name would otherwise reset every unrelated preference.
/// </summary>
public sealed class UiFontFamilyJsonConverter : JsonConverter<UiFontFamily>
{
    public override UiFontFamily Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            return UiFontFamily.System;

        var name = reader.GetString();

        // Enum.TryParse also accepts numeric strings, which would resolve to an ordinal that no
        // longer identifies the same font, so only defined member names are accepted.
        if (string.IsNullOrEmpty(name) || char.IsAsciiDigit(name[0]))
            return UiFontFamily.System;

        return Enum.TryParse<UiFontFamily>(name, ignoreCase: true, out var font) && Enum.IsDefined(font)
            ? font
            : UiFontFamily.System;
    }

    public override void Write(Utf8JsonWriter writer, UiFontFamily value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
