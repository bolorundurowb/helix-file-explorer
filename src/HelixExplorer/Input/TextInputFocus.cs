using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace HelixExplorer.Input;

/// <summary>
/// Whether the user is currently typing into a text field, so global file shortcuts can stand aside.
/// </summary>
public static class TextInputFocus
{
    /// <summary>
    /// True when a text box in the window the user is actually working in has focus.
    /// </summary>
    /// <remarks>
    /// Resolved against the active window rather than the app's main window. With a second window open,
    /// checking only the main window let its unfocused state answer for the focused one, so an inline
    /// rename in a secondary window would lose Ctrl+Z (and Ctrl+A, Ctrl+C, and every other gesture
    /// gated on this) to the file commands.
    /// </remarks>
    public static bool IsActive()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        return FindActiveWindow(desktop)?.FocusManager?.GetFocusedElement() is TextBox;
    }

    private static Window? FindActiveWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        foreach (var window in desktop.Windows)
        {
            if (window.IsActive)
                return window;
        }

        // No window is active when the app has lost focus entirely. The main window is then the best
        // guess, and matches the previous behaviour.
        return desktop.MainWindow;
    }
}
