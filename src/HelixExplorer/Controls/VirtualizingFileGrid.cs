using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        if (_rows is not null)
            _rows.SelectionChanged -= OnRowsSelectionChanged;

        _rows = e.NameScope.Find<ListBox>("PART_Rows");
        if (_rows is not null)
            _rows.SelectionChanged += OnRowsSelectionChanged;

        ApplyRowTemplate();
        UpdateItemsSubscription(null, ItemsSource);
        ScheduleRebuildRows();
    }

    private void OnRowsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Rows are layout containers only; tile selection is owned by EntryItemViewModel.
        if (_rows?.SelectedItem is not null)
            _rows.SelectedItem = null;
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
            _rows.SelectedItem = null;
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
        // Rows are layout-only; never keep ListBox selection chrome after recycle/rebuild.
        _rows.SelectedItem = null;
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
    /// Collects realized tiles whose bounds intersect <paramref name="rectInGridSpace"/> (this control's
    /// local space). Walks only horizontal row StackPanel children — not nested EntryVisualView
    /// descendants — so marquee hit-testing matches painted tile boxes through scroll/virtualization.
    /// </summary>
    public void CollectEntriesInRect(Rect rectInGridSpace, List<EntryItemViewModel> hits)
    {
        if (_rows is null || rectInGridSpace.Width < 1 || rectInGridSpace.Height < 1)
            return;

        var viewport = new Rect(Bounds.Size);
        var clip = rectInGridSpace.Intersect(viewport);
        if (clip.Width < 1 || clip.Height < 1)
            return;

        foreach (var descendant in _rows.GetVisualDescendants())
        {
            // Row template root is a horizontal StackPanel whose children are the tile Borders.
            if (descendant is not StackPanel { Orientation: Orientation.Horizontal } row)
                continue;

            foreach (var child in row.Children)
            {
                if (child is not Control { DataContext: EntryItemViewModel entry, IsVisible: true } tile)
                    continue;

                if (!TryGetBoundsInSpace(tile, this, out var bounds))
                    continue;

                bounds = bounds.Intersect(viewport);
                if (bounds.Width < 1 || bounds.Height < 1)
                    continue;

                if (clip.Intersects(bounds) && !hits.Contains(entry))
                    hits.Add(entry);
            }
        }
    }

    private static bool TryGetBoundsInSpace(Control control, Visual space, out Rect bounds)
    {
        bounds = default;
        var width = control.Bounds.Width;
        var height = control.Bounds.Height;
        if (width < 1 || height < 1 || double.IsNaN(width) || double.IsNaN(height))
            return false;

        var matrix = control.TransformToVisual(space);
        if (matrix is null)
            return false;

        var m = matrix.Value;
        var p0 = m.Transform(new Point(0, 0));
        var p1 = m.Transform(new Point(width, 0));
        var p2 = m.Transform(new Point(0, height));
        var p3 = m.Transform(new Point(width, height));

        var minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        var minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        var maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        var maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        var w = maxX - minX;
        var h = maxY - minY;
        if (w < 1 || h < 1)
            return false;

        bounds = new Rect(minX, minY, w, h);
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
