namespace HelixExplorer.Core.Infrastructure;

public interface IUiHost
{
    nint GetMainWindowHandle();

    (int X, int Y) GetPointerScreenPosition();

    Task SetClipboardTextAsync(string text);
}
