using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class DualPaneView : UserControl
{
    private TabViewModel? _tab;

    public DualPaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_tab is not null) _tab.PropertyChanged -= OnTabPropertyChanged;
        _tab = DataContext as TabViewModel;
        if (_tab is not null) _tab.PropertyChanged += OnTabPropertyChanged;
        ApplyOrientation();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabViewModel.VerticalSplit) or nameof(TabViewModel.IsDualPane))
        {
            ApplyOrientation();
        }
    }

    /// <summary>Reconfigures the grid for single-pane, side-by-side, or stacked layouts.</summary>
    private void ApplyOrientation()
    {
        var dual = _tab?.IsDualPane ?? false;
        var vertical = _tab?.VerticalSplit ?? false;

        if (!dual)
        {
            LayoutGrid.ColumnDefinitions = new ColumnDefinitions("*");
            LayoutGrid.RowDefinitions = new RowDefinitions("*");
            Place(LeftHost, 0, 0);
            return;
        }

        if (vertical)
        {
            LayoutGrid.ColumnDefinitions = new ColumnDefinitions("*");
            LayoutGrid.RowDefinitions = new RowDefinitions("*,4,*");
            Place(LeftHost, 0, 0);
            Place(Splitter, 1, 0);
            Place(RightHost, 2, 0);
            Splitter.ResizeDirection = GridResizeDirection.Rows;
            Splitter.Height = 4;
            Splitter.Width = double.NaN;
        }
        else
        {
            LayoutGrid.RowDefinitions = new RowDefinitions("*");
            LayoutGrid.ColumnDefinitions = new ColumnDefinitions("*,4,*");
            Place(LeftHost, 0, 0);
            Place(Splitter, 0, 1);
            Place(RightHost, 0, 2);
            Splitter.ResizeDirection = GridResizeDirection.Columns;
            Splitter.Width = 4;
            Splitter.Height = double.NaN;
        }
    }

    private static void Place(Control c, int row, int col)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
    }

    private void OnLeftPressed(object? sender, PointerPressedEventArgs e) => _tab?.FocusPane(_tab.Left);

    private void OnRightPressed(object? sender, PointerPressedEventArgs e) => _tab?.FocusPane(_tab.Right);
}
