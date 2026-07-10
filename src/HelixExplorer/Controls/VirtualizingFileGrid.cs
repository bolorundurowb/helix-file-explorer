using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using HelixExplorer.ViewModels;

namespace HelixExplorer.Controls;

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
    private INotifyCollectionChanged? _itemsSubscription;
    private bool _rebuildScheduled;
    private int _lastColumnCount = -1;
    private int _lastItemCount;
    private string _lastItemsPathsKey = string.Empty;
    private readonly Queue<Control> _itemContainerPool = new();

    static VirtualizingFileGrid()
    {
        ItemsSourceProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, e) =>
        {
            g.UpdateItemsSubscription(e.OldValue as IEnumerable, e.NewValue as IEnumerable);
            g._itemContainerPool.Clear();
            g.ScheduleRebuildRows();
        });
        ItemSizeProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, _) => g.ScheduleRebuildRows());
        ItemTemplateProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, _) =>
        {
            g._itemContainerPool.Clear();
            g.ApplyRowTemplate();
        });
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
        UpdateItemsSubscription(null, ItemsSource);
        ScheduleRebuildRows();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromItems();
        _itemContainerPool.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.WidthChanged)
            ScheduleRebuildRows();
    }

    private void UpdateItemsSubscription(IEnumerable? oldSource, IEnumerable? newSource)
    {
        if (ReferenceEquals(oldSource, newSource))
            return;

        UnsubscribeFromItems();

        if (newSource is INotifyCollectionChanged collection)
        {
            _itemsSubscription = collection;
            _itemsSubscription.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void UnsubscribeFromItems()
    {
        if (_itemsSubscription is null)
            return;

        _itemsSubscription.CollectionChanged -= OnItemsCollectionChanged;
        _itemsSubscription = null;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ScheduleRebuildRows();

    private void ScheduleRebuildRows()
    {
        if (_rebuildScheduled)
            return;

        _rebuildScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildScheduled = false;
            RebuildRows();
        }, DispatcherPriority.Loaded);
    }

    private void ApplyRowTemplate()
    {
        if (_rows is null || ItemTemplate is null)
            return;

        _rows.ItemTemplate = new FuncDataTemplate<GridRow>((row, _) =>
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            void UpdateStackChildren(StackPanel s, GridRow? gridRow)
            {
                if (gridRow?.Items is null || ItemTemplate is null)
                {
                    foreach (var child in s.Children)
                    {
                        if (child is Control c)
                        {
                            c.DataContext = null;
                            _itemContainerPool.Enqueue(c);
                        }
                    }
                    s.Children.Clear();
                    return;
                }

                var items = gridRow.Items;
                var currentChildCount = s.Children.Count;

                if (currentChildCount < items.Count)
                {
                    for (int i = currentChildCount; i < items.Count; i++)
                    {
                        Control? content = null;
                        if (_itemContainerPool.Count > 0)
                        {
                            content = _itemContainerPool.Dequeue();
                        }
                        else
                        {
                            content = ItemTemplate.Build(items[i]);
                        }

                        if (content is not null)
                        {
                            s.Children.Add(content);
                        }
                    }
                }
                else if (currentChildCount > items.Count)
                {
                    for (int i = currentChildCount - 1; i >= items.Count; i--)
                    {
                        var child = s.Children[i];
                        s.Children.RemoveAt(i);
                        if (child is Control c)
                        {
                            c.DataContext = null;
                            _itemContainerPool.Enqueue(c);
                        }
                    }
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (i < s.Children.Count)
                    {
                        s.Children[i].DataContext = items[i];
                    }
                }
            }

            stack.DataContextChanged += (sender, e) =>
            {
                if (sender is StackPanel s)
                {
                    UpdateStackChildren(s, s.DataContext as GridRow);
                }
            };

            stack.DetachedFromVisualTree += (sender, e) =>
            {
                if (sender is StackPanel s)
                {
                    foreach (var child in s.Children)
                    {
                        if (child is Control c)
                        {
                            c.DataContext = null;
                            _itemContainerPool.Enqueue(c);
                        }
                    }
                    s.Children.Clear();
                }
            };

            UpdateStackChildren(stack, row);

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
            _lastColumnCount = -1;
            _lastItemCount = 0;
            _lastItemsPathsKey = string.Empty;
            return;
        }

        var viewportWidth = _scrollViewer?.Viewport.Width ?? Bounds.Width;
        if (viewportWidth <= 0)
            viewportWidth = 800;

        var tileWidth = Math.Max(48, ItemSize + 12);
        var columns = Math.Max(1, (int)(viewportWidth / tileWidth));
        var rowCount = (items.Count + columns - 1) / columns;
        var pathsKey = BuildItemsPathsKey(items);

        if (columns == _lastColumnCount
            && items.Count == _lastItemCount
            && pathsKey == _lastItemsPathsKey
            && _rows.ItemsSource is IList<GridRow> existing
            && existing.Count == rowCount)
            return;

        var rows = new List<GridRow>(rowCount);
        for (var i = 0; i < items.Count; i += columns)
        {
            var count = Math.Min(columns, items.Count - i);
            rows.Add(new GridRow(items.GetRange(i, count)));
        }

        _rows.ItemsSource = rows;
        _lastColumnCount = columns;
        _lastItemCount = items.Count;
        _lastItemsPathsKey = pathsKey;
    }

    private static string BuildItemsPathsKey(IReadOnlyList<object> items)
    {
        if (items.Count == 0)
            return string.Empty;

        return string.Join('\n', items.Select(GetItemPathKey));
    }

    private static string GetItemPathKey(object item)
        => item is EntryItemViewModel entry ? entry.FullPath : item.GetHashCode().ToString();

    private sealed class GridRow(IReadOnlyList<object> items)
    {
        public IReadOnlyList<object> Items { get; } = items;
    }
}
