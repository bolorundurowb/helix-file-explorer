using Avalonia;
using Avalonia.Media;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Services;

public sealed class AvaloniaUiFontService : IUiFontService
{
    public UiFontFamily Current { get; private set; } = UiFontFamily.System;

    public void ApplyFont(UiFontFamily font)
    {
        Current = font;

        if (Application.Current?.Resources is null)
            return;

        var family = new FontFamily(UiFontCatalog.ResolveFontFamilySource(font));
        Application.Current.Resources["ContentControlThemeFontFamily"] = family;
    }
}
