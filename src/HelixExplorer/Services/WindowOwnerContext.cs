using Avalonia.Controls;

namespace HelixExplorer.Services;

/// <summary>Scoped owner window for per-window UI services (dialogs, shell HWND).</summary>
public interface IWindowOwnerContext
{
    Window? OwnerWindow { get; }

    void SetOwner(Window window);
}

public sealed class WindowOwnerContext : IWindowOwnerContext
{
    public Window? OwnerWindow { get; private set; }

    public void SetOwner(Window window) => OwnerWindow = window;
}
