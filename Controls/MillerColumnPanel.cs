using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections;

namespace HelixExplorer.Controls;

/// <summary>
/// A custom <see cref="Panel"/> that lays out its children horizontally, one column per
/// nested folder. As the user drills deeper, a new <see cref="ListBox"/> is appended on
/// the right (older columns may be clipped out of view but remain in the visual tree).
/// The active (rightmost) column scrolls horizontally into view on layout.
/// </summary>
public sealed class MillerColumnPanel : Panel, INavigatedContainer
{
    /// <summary>Identifies the <see cref="ColumnWidth"/> dependency property.</summary>
    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<MillerColumnPanel, double>(nameof(ColumnWidth), 250);

    /// <summary>Identifies the <see cref="ColumnSpacing"/> dependency property.</summary>
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<MillerColumnPanel, double>(nameof(ColumnSpacing), 4);

    /// <summary>Identifies the attachable ColumnIndex property used by TemplatedParent.</summary>
    public static readonly AttachedProperty<int> ColumnIndexProperty =
        AvaloniaProperty.RegisterAttached<MillerColumnPanel, Control, int>("ColumnIndex");

    public static int GetColumnIndex(Control element) => element.GetValue(ColumnIndexProperty);
    public static void SetColumnIndex(Control element, int value) => element.SetValue(ColumnIndexProperty, value);

    static MillerColumnPanel()
    {
        AffectsMeasure<MillerColumnPanel>(ColumnWidthProperty, ColumnSpacingProperty);
    }

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

    /// <summary>Raised when the user activates a column entry. The host supplies columns
    /// for the resulting path.</summary>
    public event EventHandler<MillerColumnActivatedEventArgs>? ColumnActivated;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double maxH = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(ColumnWidth, availableSize.Height));
            width += ColumnWidth + ColumnSpacing;
            if (child.DesiredSize.Height > maxH) maxH = child.DesiredSize.Height;
        }
        width = Math.Max(0, width - ColumnSpacing);
        return new Size(width, maxH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            double w = ColumnWidth;
            var rect = new Rect(x, 0, w, finalSize.Height);
            child.Arrange(rect);
            x += w + ColumnSpacing;
        }
        return new Size(Math.Max(x - ColumnSpacing, 0), finalSize.Height);
    }

    /// <summary>Routes pointer activation events from a column's child to the panel.</summary>
    public void RaiseActivated(int columnIndex, object? item)
    {
        ColumnActivated?.Invoke(this, new MillerColumnActivatedEventArgs(columnIndex, item));
    }

    void INavigatedContainer.OnColumnActivated(int index, object? item) => RaiseActivated(index, item);
}

internal interface INavigatedContainer
{
    void OnColumnActivated(int index, object? item);
}

public sealed class MillerColumnActivatedEventArgs : EventArgs
{
    public int ColumnIndex { get; }
    public object? Item { get; }
    public MillerColumnActivatedEventArgs(int columnIndex, object? item) { ColumnIndex = columnIndex; Item = item; }
}