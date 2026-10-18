using Avalonia;
using Avalonia.Media;
using HelixExplorer.Core.Theming;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Services;

public sealed class AvaloniaUiFontService(ILogger<AvaloniaUiFontService> logger) : IUiFontService
{
    public UiFontFamily Current { get; private set; } = UiFontFamily.System;

    public void ApplyFont(UiFontFamily font)
    {
        Current = font;

        if (Application.Current?.Resources is null)
            return;

        var source = UiFontCatalog.ResolveFontFamilySource(font);
        try
        {
            Application.Current.Resources["ContentControlThemeFontFamily"] = new FontFamily(source);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Applying UI font {Font} ({Source}) failed; falling back to the system font.", font, source);
            Current = UiFontFamily.System;
            Application.Current.Resources["ContentControlThemeFontFamily"] =
                new FontFamily(UiFontCatalog.GetSystemFontFamilySource());
        }
    }
}
