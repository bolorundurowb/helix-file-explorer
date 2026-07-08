namespace HelixExplorer.Core.Infrastructure;

/// <summary>UI-platform bridge for native shell popups that need an HWND and screen coords.</summary>
public interface IUiHost
{
    nint GetMainWindowHandle();

    (int X, int Y) GetPointerScreenPosition();

    Task SetClipboardTextAsync(string text);
}
