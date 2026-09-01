using Avalonia.Media;
using Avalonia.Media.Immutable;
using HelixExplorer.Converters;
using HelixExplorer.Core.Models;

namespace HelixExplorer.ViewModels.Tests;

public class EntryIconBrushConverterTests
{
    [Fact]
    public void Convert_UnknownType_ReturnsFallbackBrush()
    {
        var converter = new EntryIconBrushConverter();
        var result = converter.Convert(null, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture);
        (result is ImmutableSolidColorBrush).Must().BeTrue();
    }

    [Fact]
    public void Convert_PdfEntry_UsesCachedBrush()
    {
        var converter = new EntryIconBrushConverter();
        var entry = new FileSystemEntry(@"C:\a.pdf", "a.pdf", false, 1, DateTime.UtcNow, ".pdf", false);
        var first = converter.Convert(entry, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture);
        var second = converter.Convert(entry, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture);
        ReferenceEquals(first, second).Must().BeTrue();
    }
}
