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

    private ListBox? _rows;
    private INotifyCollectionChanged? _itemsSubscription;
    private bool _rebuildScheduled;
    private int _lastColumnCount = -1;
    private int _lastItemCount;
    private string _lastItemsPathsKey = string.Empty;

    static VirtualizingFileGrid()
    {
        ItemsSourceProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, e) =>
        {
            g.UpdateItemsSubscription(e.OldValue as IEnumerable, e.NewValue as IEnumerable);
            g.ScheduleRebuildRows();
        });
        ItemSizeProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, _) => g.ScheduleRebuildRows());
        ItemTemplateProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, _) =>
        {
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
        _rows = e.NameScope.Find<ListBox>("PART_Rows");
        ApplyRowTemplate();
        UpdateItemsSubscription(null, ItemsSource);
        ScheduleRebuildRows();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromItems();
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
                        var content = ItemTemplate.Build(items[i]);
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

        var viewportWidth = Bounds.Width;
        if (viewportWidth <= 0)
            viewportWidth = 800;

        var columns = GetColumnCount(viewportWidth);
        var rowCount = (items.Count + columns - 1) / columns;
        if (columns == _lastColumnCount && items.Count == _lastItemCount)
        {
            var currentPathsKey = BuildItemsPathsKey(items);
            if (currentPathsKey == _lastItemsPathsKey
                && _rows.ItemsSource is IList<GridRow> existing
                && existing.Count == rowCount)
                return;
        }

        var pathsKey = BuildItemsPathsKey(items);

        var rows = new List<GridRow>(rowCount);
        for (var i = 0; i < items.Count; i += columns)
        {
            var count = Math.Min(columns, items.Count - i);
            // Slice view over the shared flat list instead of GetRange, which allocated and copied a
            // new List<object> per row on every rebuild (once per resize / selection refresh).
            rows.Add(new GridRow(items, i, count));
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

    public int GetColumnCount(double viewportWidth)
    {
        if (viewportWidth <= 0)
            viewportWidth = 800;

        return Math.Max(1, (int)(viewportWidth / Math.Max(48, ItemSize + 12)));
    }

    public bool TryGetAdjacentIndex(int currentIndex, int itemCount, Avalonia.Input.Key direction, out int targetIndex)
    {
        targetIndex = currentIndex;
        if ((uint)currentIndex >= (uint)itemCount)
            return false;

        var columns = GetColumnCount(Bounds.Width);
        var candidate = direction switch
        {
            Avalonia.Input.Key.Left => currentIndex - 1,
            Avalonia.Input.Key.Right => currentIndex + 1,
            Avalonia.Input.Key.Up => currentIndex - columns,
            Avalonia.Input.Key.Down => currentIndex + columns,
            _ => currentIndex
        };

        if ((uint)candidate >= (uint)itemCount)
            return false;

        targetIndex = candidate;
        return true;
    }

    /// <summary>
    /// A lightweight, zero-copy window over a contiguous span of the shared flat item list. Holding
    /// a reference plus offset avoids the per-row list allocation/copy that <c>GetRange</c> incurred.
    /// </summary>
    private sealed class GridRow(IReadOnlyList<object> source, int start, int count) : IReadOnlyList<object>
    {
        public IReadOnlyList<object> Items => this;

        public int Count => count;

        public object this[int index]
        {
            get
            {
                if ((uint)index >= (uint)count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return source[start + index];
            }
        }

        public IEnumerator<object> GetEnumerator()
        {
            for (var i = 0; i < count; i++)
                yield return source[start + i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
