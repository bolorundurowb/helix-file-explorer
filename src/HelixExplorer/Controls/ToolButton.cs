using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace HelixExplorer.Controls;

public sealed class ToolButton : Button
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ToolButton, string?>(nameof(Label));

    public static readonly StyledProperty<string?> GestureProperty =
        AvaloniaProperty.Register<ToolButton, string?>(nameof(Gesture));

    // Without this, Avalonia keys styles on ToolButton and Button.toolButton (padding, hover, disabled) never applies.
    protected override Type StyleKeyOverride => typeof(Button);

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    static ToolButton()
    {
        LabelProperty.Changed.AddClassHandler<ToolButton>((button, _) => button.ApplyAccessibility());
        GestureProperty.Changed.AddClassHandler<ToolButton>((button, _) => button.ApplyAccessibility());
    }

    public ToolButton()
    {
        // Shares Button.toolButton so disabled chrome stays the one toolbar style.
        if (!Classes.Contains("toolButton"))
            Classes.Add("toolButton");
    }

    private void ApplyAccessibility()
    {
        if (string.IsNullOrEmpty(Label))
            ClearValue(AutomationProperties.NameProperty);
        else
            AutomationProperties.SetName(this, Label);

        if (string.IsNullOrEmpty(Gesture))
        {
            ClearValue(AutomationProperties.AcceleratorKeyProperty);
            ToolTip.SetTip(this, string.IsNullOrEmpty(Label) ? null : Label);
            return;
        }

        AutomationProperties.SetAcceleratorKey(this, Gesture);
        ToolTip.SetTip(this, string.IsNullOrEmpty(Label) ? Gesture : $"{Label} ({Gesture})");
    }
}
