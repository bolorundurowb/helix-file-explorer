using Avalonia.Input;

namespace HelixExplorer.Input;

public enum RenameKeyAction
{
    None,

    Commit,

    Cancel,

    /// <summary>
    /// A caret/selection gesture that must stay inside the rename TextBox. The editor keeps its
    /// built-in handling of the key, but the event must be marked handled so it does not bubble to
    /// the surrounding list and move the item selection.
    /// </summary>
    Contain
}

/// <summary>
/// Pure decision logic for inline-rename key handling, extracted from the view so it can be unit tested.
/// While rename mode is active, navigation and text-editing gestures must be contained within the editor
/// instead of leaking to pane/list selection.
/// </summary>
public static class RenameKeyGesture
{
    public static RenameKeyAction Resolve(Key key, KeyModifiers modifiers)
    {
        // Enter commits and Escape cancels regardless of modifiers (rename is single-line).
        if (key is Key.Enter)
            return RenameKeyAction.Commit;

        if (key is Key.Escape)
            return RenameKeyAction.Cancel;

        // Navigation / caret-movement keys (including modified variants such as Shift+Left to extend a
        // selection or Ctrl+Left to jump a word) must remain text-editing gestures inside the editor.
        return key switch
        {
            Key.Left or Key.Right or Key.Home or Key.End
                or Key.Up or Key.Down or Key.PageUp or Key.PageDown => RenameKeyAction.Contain,
            _ => RenameKeyAction.None
        };
    }
}
