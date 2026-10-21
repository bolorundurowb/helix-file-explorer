using Avalonia.Controls;

namespace HelixExplorer.Controls;

/// <summary>
/// A <see cref="MenuFlyout"/> whose items are generated each time it opens.
/// </summary>
/// <remarks>
/// A plain empty <see cref="MenuFlyout"/> cannot be filled from <c>Opening</c>. Its <see cref="ItemCollection"/>
/// starts on Avalonia's shared "uninitialised" sentinel array, and the presenter created on first open unwraps
/// <c>ItemsSource = Items</c> to that sentinel. The first <c>Add</c> then swaps the flyout onto a fresh list the
/// presenter never hears about, so the popup opens empty (invisible) and its light-dismiss layer swallows the next
/// click. Touching the collection once here gives it its real backing list before any presenter binds to it.
/// </remarks>
public sealed class CommandMenuFlyout : MenuFlyout
{
    public CommandMenuFlyout()
    {
        Items.Add(null);
        Items.Clear();
    }
}
