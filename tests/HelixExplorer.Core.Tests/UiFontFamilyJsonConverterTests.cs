using System.Text.Json;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests;

public class UiFontFamilyJsonConverterTests
{
    [Theory]
    [InlineData("\"System\"", UiFontFamily.System)]
    [InlineData("\"Inter\"", UiFontFamily.Inter)]
    [InlineData("\"Figtree\"", UiFontFamily.Figtree)]
    [InlineData("\"IbmPlexSans\"", UiFontFamily.IbmPlexSans)]
    [InlineData("\"GeistSans\"", UiFontFamily.GeistSans)]
    public void Read_shouldReturnTheStoredFont_whenTheNameIsStillInTheCatalog(string json, UiFontFamily expected)
    {
        JsonSerializer.Deserialize<UiFontFamily>(json).Must().Be(expected);
    }

    [Theory]
    [InlineData("\"SourceSans3\"")]
    [InlineData("\"sourcesans3\"")]
    public void Read_shouldReturnFigtree_whenTheStoredFontIsSourceSans3(string json)
    {
        JsonSerializer.Deserialize<UiFontFamily>(json).Must().Be(UiFontFamily.Figtree);
    }

    [Theory]
    [InlineData("\"DmSans\"")]
    [InlineData("\"PlusJakartaSans\"")]
    [InlineData("\"Seafoam\"")]
    [InlineData("\"\"")]
    [InlineData("\"2\"")]
    [InlineData("2")]
    [InlineData("null")]
    public void Read_shouldFallBackToSystem_whenTheStoredValueDoesNotIdentifyACurrentFont(string json)
    {
        JsonSerializer.Deserialize<UiFontFamily>(json).Must().Be(UiFontFamily.System);
    }

    [Fact]
    public void Write_shouldPersistTheMemberName()
    {
        JsonSerializer.Serialize(UiFontFamily.GeistSans).Must().Be("\"GeistSans\"");
    }
}
