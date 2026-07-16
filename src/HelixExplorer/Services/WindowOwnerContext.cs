using Avalonia.Controls;

namespace HelixExplorer.Services;

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
