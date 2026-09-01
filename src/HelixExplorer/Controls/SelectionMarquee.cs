using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HelixExplorer.Controls;

public sealed class SelectionMarquee : Control
{
    private static readonly IImmutableSolidColorBrush Fill = new ImmutableSolidColorBrush(Color.FromArgb(40, 0, 120, 212));
    private static readonly Pen BorderPen = new(new ImmutableSolidColorBrush(Color.FromArgb(200, 0, 120, 212)), 1, dashStyle: DashStyle.Dash);
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<SelectionMarquee, bool>(nameof(IsActive));

    public static readonly StyledProperty<Rect> SelectionRectProperty =
        AvaloniaProperty.Register<SelectionMarquee, Rect>(nameof(SelectionRect));

    static SelectionMarquee()
    {
        AffectsRender<SelectionMarquee>(SelectionRectProperty, IsActiveProperty);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Rect SelectionRect
    {
        get => GetValue(SelectionRectProperty);
        set => SetValue(SelectionRectProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (!IsActive || SelectionRect.Width < 1 || SelectionRect.Height < 1)
            return;

        context.DrawRectangle(Fill, BorderPen, SelectionRect);
    }
}
