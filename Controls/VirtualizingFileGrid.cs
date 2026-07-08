using Avalonia;
using Avalonia.Controls;

namespace HelixExplorer.Controls;

/// <summary>
/// Hosts a wrap-laid-out collection of file tiles for the Grid/Icon view-mode.
/// Inherits from <see cref="ItemsControl"/>; consumers bind <c>Items</c> directly in
/// XAML and supply their own <c>ItemsPanelTemplate</c> (typically a <c>WrapPanel</c>).
/// Exposes <see cref="ItemWidth"/>/<see cref="ItemHeight"/> styling properties that
/// the host can bind when rendering tiles.
/// </summary>
public sealed class VirtualizingFileGrid : ItemsControl
{
    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<VirtualizingFileGrid, double>(nameof(ItemWidth), 96);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<VirtualizingFileGrid, double>(nameof(ItemHeight), 96);

    public double ItemWidth
    {
        get => GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }
}