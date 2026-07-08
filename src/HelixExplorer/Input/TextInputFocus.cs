using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace HelixExplorer.Input;

/// <summary>Detects when keyboard focus is in a text-editing control.</summary>
public static class TextInputFocus
{
    public static bool IsActive()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        return desktop.MainWindow?.FocusManager?.GetFocusedElement() is TextBox;
    }
}
