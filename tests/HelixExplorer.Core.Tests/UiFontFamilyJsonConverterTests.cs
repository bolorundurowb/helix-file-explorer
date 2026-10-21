using System.Text.Json;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Core.Tests;

public class UiFontFamilyJsonConverterTests
{
    [Theory]
    [InlineData("\"System\"", UiFontFamily.System)]
    [InlineData("\"Inter\"", UiFontFamily.Inter)]
    [InlineData("\"SourceSans3\"", UiFontFamily.SourceSans3)]
    [InlineData("\"IbmPlexSans\"", UiFontFamily.IbmPlexSans)]
    [InlineData("\"GeistSans\"", UiFontFamily.GeistSans)]
    public void Read_shouldReturnTheStoredFont_whenTheNameIsStillInTheCatalog(string json, UiFontFamily expected)
    {
        JsonSerializer.Deserialize<UiFontFamily>(json).Must().Be(expected);
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
