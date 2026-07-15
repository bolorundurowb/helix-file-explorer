using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Sorting;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Controls;

public sealed partial class SortColumnHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SortColumnHeader, string>(nameof(Title));

    public static readonly StyledProperty<SortColumn> ColumnProperty =
        AvaloniaProperty.Register<SortColumnHeader, SortColumn>(nameof(Column));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public SortColumn Column
    {
        get => GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    public SortColumnHeader()
    {
        InitializeComponent();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (e.InitialPressMouseButton != MouseButton.Left || DataContext is not PaneViewModel pane)
            return;

        var (nextColumn, nextDescending) = SortSelection.Toggle(pane.SortColumn, pane.SortDescending, Column);
        pane.SortColumn = nextColumn;
        pane.SortDescending = nextDescending;

        e.Handled = true;
    }
}
