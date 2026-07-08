using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Controls;

/// <summary>
/// Grid view that virtualizes by row so large folders stay responsive.
/// </summary>
public sealed class VirtualizingFileGrid : TemplatedControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<VirtualizingFileGrid, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<double> ItemSizeProperty =
        AvaloniaProperty.Register<VirtualizingFileGrid, double>(nameof(ItemSize), 96);

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<VirtualizingFileGrid, IDataTemplate?>(nameof(ItemTemplate));

    private ItemsControl? _rows;
    private ScrollViewer? _scrollViewer;

    static VirtualizingFileGrid()
    {
        ItemsSourceProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, _) => g.RebuildRows());
        ItemSizeProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, _) => g.RebuildRows());
        ItemTemplateProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, _) => g.ApplyRowTemplate());
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double ItemSize
    {
        get => GetValue(ItemSizeProperty);
        set => SetValue(ItemSizeProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _rows = e.NameScope.Find<ItemsControl>("PART_Rows");
        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        ApplyRowTemplate();
        RebuildRows();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.WidthChanged)
            RebuildRows();
    }

    private void ApplyRowTemplate()
    {
        if (_rows is null || ItemTemplate is null)
            return;

        _rows.ItemTemplate = new FuncDataTemplate<GridRow>((row, _) =>
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            if (row?.Items is null || ItemTemplate is null)
                return stack;

            foreach (var item in row.Items)
            {
                var content = ItemTemplate.Build(item);
                if (content is null)
                    continue;

                content.DataContext = item;
                stack.Children.Add(content);
            }

            return stack;
        });
    }

    private void RebuildRows()
    {
        if (_rows is null)
            return;

        var items = ItemsSource?.Cast<object>().ToList() ?? [];
        if (items.Count == 0)
        {
            _rows.ItemsSource = Array.Empty<GridRow>();
            return;
        }

        var viewportWidth = _scrollViewer?.Viewport.Width ?? Bounds.Width;
        if (viewportWidth <= 0)
            viewportWidth = 800;

        var tileWidth = Math.Max(48, ItemSize + 12);
        var columns = Math.Max(1, (int)(viewportWidth / tileWidth));
        var rows = new List<GridRow>((items.Count + columns - 1) / columns);
        for (var i = 0; i < items.Count; i += columns)
        {
            var count = Math.Min(columns, items.Count - i);
            rows.Add(new GridRow(items.GetRange(i, count)));
        }

        _rows.ItemsSource = rows;
    }

    private sealed class GridRow(IReadOnlyList<object> items)
    {
        public IReadOnlyList<object> Items { get; } = items;
    }
}
