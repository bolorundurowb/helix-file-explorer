using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Views;

public sealed partial class DualPaneView : UserControl
{
    private const double SplitterSize = 6;

    private TabViewModel? _tab;

    public DualPaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ApplyOrientation(PaneSplitOrientation.Vertical);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_tab is not null)
            _tab.PropertyChanged -= OnTabPropertyChanged;

        _tab = DataContext as TabViewModel;
        if (_tab is not null)
        {
            _tab.PropertyChanged += OnTabPropertyChanged;
            ApplyOrientation(_tab.Orientation);
        }
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabViewModel.Orientation) or nameof(TabViewModel.IsDualPane)
            && _tab is not null)
        {
            ApplyOrientation(_tab.Orientation);
        }
    }

    private void ApplyOrientation(PaneSplitOrientation orientation)
    {
        DualGrid.ColumnDefinitions.Clear();
        DualGrid.RowDefinitions.Clear();

        if (orientation == PaneSplitOrientation.Vertical)
        {
            DualGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            DualGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            DualGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            Grid.SetColumn(LeftHost, 0);
            Grid.SetRow(LeftHost, 0);
            Grid.SetColumn(Splitter, 1);
            Grid.SetRow(Splitter, 0);
            Grid.SetColumn(RightHost, 2);
            Grid.SetRow(RightHost, 0);

            Splitter.Width = SplitterSize;
            Splitter.Height = double.NaN;
            Splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            Splitter.VerticalAlignment = VerticalAlignment.Stretch;
        }
        else
        {
            DualGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            DualGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            DualGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

            Grid.SetRow(LeftHost, 0);
            Grid.SetColumn(LeftHost, 0);
            Grid.SetRow(Splitter, 1);
            Grid.SetColumn(Splitter, 0);
            Grid.SetRow(RightHost, 2);
            Grid.SetColumn(RightHost, 0);

            Splitter.Height = SplitterSize;
            Splitter.Width = double.NaN;
            Splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            Splitter.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    private void OnLeftActivate(object? sender, PointerPressedEventArgs e)
    {
        if (_tab is not null)
            _tab.SetActivePane(_tab.LeftPane);
    }

    private void OnRightActivate(object? sender, PointerPressedEventArgs e)
    {
        if (_tab?.RightPane is { } right)
            _tab.SetActivePane(right);
    }
}
