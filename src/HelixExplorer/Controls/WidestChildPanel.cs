using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace HelixExplorer.Controls;

/// <summary>
/// Uses the widest child's desired width for every child, and arranges only
/// <see cref="ActiveIndex"/>. Settings sections stay in the tree so a narrow
/// page still gets a frame wide enough for the widest page, without stacking
/// their heights.
/// </summary>
public sealed class WidestChildPanel : Panel
{
    public static readonly StyledProperty<int> ActiveIndexProperty =
        AvaloniaProperty.Register<WidestChildPanel, int>(nameof(ActiveIndex));

    static WidestChildPanel()
    {
        AffectsMeasure<WidestChildPanel>(ActiveIndexProperty);
        ActiveIndexProperty.Changed.AddClassHandler<WidestChildPanel>((panel, _) => panel.UpdateChildState());
    }

    public WidestChildPanel()
    {
        ClipToBounds = true;
        Children.CollectionChanged += (_, _) => UpdateChildState();
    }

    public int ActiveIndex
    {
        get => GetValue(ActiveIndexProperty);
        set => SetValue(ActiveIndexProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxWidth = 0.0;
        var activeHeight = 0.0;
        var active = ActiveIndex;

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(availableSize);
            if (child.DesiredSize.Width > maxWidth)
                maxWidth = child.DesiredSize.Width;
            if (i == active)
                activeHeight = child.DesiredSize.Height;
        }

        return new Size(maxWidth, activeHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Inactive children get the same slot rather than an empty rect: a StackPanel
        // still lays its rows out at desired size inside a zero-size arrange, so an
        // empty rect would paint every section over the active one. They are hidden
        // via Opacity (see UpdateChildState) and clipped by this panel.
        var slot = new Rect(0, 0, finalSize.Width, finalSize.Height);
        for (var i = 0; i < Children.Count; i++)
            Children[i].Arrange(slot);

        return finalSize;
    }

    private void UpdateChildState()
    {
        var active = ActiveIndex;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var isActive = i == active;
            // IsVisible would zero the desired size and defeat the shared-width measure.
            child.Opacity = isActive ? 1 : 0;
            child.IsEnabled = isActive;
            child.IsHitTestVisible = isActive;
            if (isActive)
                child.ClearValue(AutomationProperties.AccessibilityViewProperty);
            else
                AutomationProperties.SetAccessibilityView(child, AccessibilityView.Raw);
        }
    }
}
