using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Input;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.Views;

/// <summary>
/// Null accent is the default/OS swatch. A stored colour selects only the matching swatch.
/// </summary>
public sealed class AccentSwatchSelectedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => AccentSwatchSelection.Matches(value, parameter as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public static class AccentSwatchSelection
{
    public static bool Matches(object? accentValue, string? swatchKey)
    {
        var isDefaultSwatch = string.IsNullOrEmpty(swatchKey)
                              || string.Equals(swatchKey, "default", StringComparison.Ordinal);

        if (accentValue is not uint argb)
            return isDefaultSwatch && accentValue is null;

        if (isDefaultSwatch)
            return false;

        try
        {
            return argb == AccentColorDefaults.FromHex(swatchKey!);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

/// <summary>
/// Writes Avalonia <see cref="KeyGesture"/> invariant syntax, the same string the terminal binding parses.
/// </summary>
public static class TerminalGestureSyntax
{
    public static bool TryFormat(Key key, KeyModifiers modifiers, out string gesture)
    {
        gesture = string.Empty;
        if (!IsChordKey(key))
            return false;

        gesture = new KeyGesture(key, modifiers).ToString();
        return true;
    }

    private static bool IsChordKey(Key key) => key is not (
        Key.None
        or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin
        or Key.DeadCharProcessed
        or Key.ImeProcessed
        or Key.System);
}

/// <summary>
/// The terminal command's own default is not a conflict. Every other built-in shortcut is,
/// including pane handlers that are not window <see cref="KeyBinding"/>s (Enter, Back, F5, Escape).
/// </summary>
public static class BuiltInGestureConflict
{
    private static readonly (string Gesture, string Command)[] Bindings =
    [
        ("Ctrl+Shift+P", "Command palette"),
        ("Ctrl+Shift+T", "Reopen closed tab"),
        ("Ctrl+N", "New window"),
        ("Ctrl+Shift+W", "Close window"),
        ("Ctrl+T", "New tab"),
        ("Ctrl+W", "Close tab"),
        ("Alt+Left", "Go back"),
        ("Alt+Right", "Go forward"),
        ("Alt+D", "Edit path"),
        ("Ctrl+L", "Edit path"),
        ("F4", "Edit path"),
        ("Alt+Enter", "Properties"),
        ("F6", "Cycle focus"),
        ("Ctrl+D", "Dual pane"),
        ("Ctrl+F", "Filter"),
        ("Ctrl+Shift+F", "Search"),
        ("Ctrl+Z", "Undo"),
        ("Ctrl+Y", "Redo"),
        ("Ctrl+Shift+Z", "Redo"),
        ("Ctrl+X", "Cut"),
        ("Ctrl+C", "Copy"),
        ("Ctrl+V", "Paste"),
        ("Delete", "Delete"),
        ("Shift+Delete", "Delete permanently"),
        ("Ctrl+Tab", "Next tab"),
        ("Ctrl+Shift+Tab", "Previous tab"),
        ("F2", "Rename"),
        ("Alt+Up", "Go up"),
        ("Ctrl+Shift+N", "New folder"),
        ("Ctrl+Shift+C", "Copy path"),
        ("Ctrl+A", "Select all"),
        ("Ctrl+Shift+A", "Invert selection"),
        ("Alt+F5", "Copy to the other pane"),
        ("Alt+F6", "Move to the other pane"),
        ("Ctrl+Shift+J", "Status centre"),
        ("Enter", "Open the selected item"),
        ("Back", "Go up"),
        ("F5", "Refresh"),
        ("Escape", "Clear selection"),
    ];

    public static string? Describe(string? gesture)
    {
        if (!TryParse(gesture, out var parsed) || parsed.Key == Key.None)
            return null;

        foreach (var (candidate, command) in Bindings)
        {
            if (TryParse(candidate, out var other) && parsed == other)
                return $"Already used by {command}.";
        }

        return null;
    }

    private static bool TryParse(string? gesture, out KeyGesture parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(gesture))
            return false;

        try
        {
            parsed = KeyGesture.Parse(gesture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed class TerminalGestureConflictConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var message = BuiltInGestureConflict.Describe(value as string);
        if (parameter is "visible")
            return message is not null;

        return message ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
