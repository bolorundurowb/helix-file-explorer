using Avalonia;
using Avalonia.Controls;

namespace HelixExplorer.Controls;

/// <summary>
/// Lays out children horizontally as Miller columns and routes activation to the host.
/// </summary>
public sealed class MillerColumnPanel : Panel
{
    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<MillerColumnPanel, double>(nameof(ColumnWidth), 250);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<MillerColumnPanel, double>(nameof(ColumnSpacing), 4);

    public static readonly AttachedProperty<int> ColumnIndexProperty =
        AvaloniaProperty.RegisterAttached<MillerColumnPanel, Control, int>("ColumnIndex");

    public static int GetColumnIndex(Control element) => element.GetValue(ColumnIndexProperty);

    public static void SetColumnIndex(Control element, int value) => element.SetValue(ColumnIndexProperty, value);

    static MillerColumnPanel()
        => AffectsMeasure<MillerColumnPanel>(ColumnWidthProperty, ColumnSpacingProperty);

    public double ColumnWidth
    {
        get => GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public event EventHandler<MillerColumnActivatedEventArgs>? ColumnActivated;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double maxH = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(ColumnWidth, availableSize.Height));
            width += ColumnWidth + ColumnSpacing;
            if (child.DesiredSize.Height > maxH)
                maxH = child.DesiredSize.Height;
        }

        width = Math.Max(0, width - ColumnSpacing);
        return new Size(width, maxH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var rect = new Rect(x, 0, ColumnWidth, finalSize.Height);
            child.Arrange(rect);
            x += ColumnWidth + ColumnSpacing;
        }

        return new Size(Math.Max(x - ColumnSpacing, 0), finalSize.Height);
    }

    public void RaiseActivated(int columnIndex, object? item)
        => ColumnActivated?.Invoke(this, new MillerColumnActivatedEventArgs(columnIndex, item));
}

public sealed class MillerColumnActivatedEventArgs : EventArgs
{
    public MillerColumnActivatedEventArgs(int columnIndex, object? item)
    {
        ColumnIndex = columnIndex;
        Item = item;
    }

    public int ColumnIndex { get; }
    public object? Item { get; }
}
