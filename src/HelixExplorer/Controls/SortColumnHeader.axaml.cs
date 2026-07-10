using Avalonia;
using Avalonia.Controls;
using HelixExplorer.Core.Models;

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
}
