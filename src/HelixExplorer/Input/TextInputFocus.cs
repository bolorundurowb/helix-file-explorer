using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace HelixExplorer.Input;

public static class TextInputFocus
{
    public static bool IsActive()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        return desktop.MainWindow?.FocusManager?.GetFocusedElement() is TextBox;
    }
}
