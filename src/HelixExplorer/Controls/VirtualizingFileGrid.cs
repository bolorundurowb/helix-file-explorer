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

    /// <summary>
    /// Template for <see cref="GroupHeaderViewModel"/> bands. Header rows are laid out full width
    /// instead of being packed into the uniform tile columns.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<VirtualizingFileGrid, IDataTemplate?>(nameof(HeaderTemplate));

    private ListBox? _rows;
    private INotifyCollectionChanged? _itemsSubscription;
    private bool _rebuildScheduled;
    private int _lastColumnCount = -1;
    private int _lastItemCount;
    private int _lastItemsFingerprint;
    private List<object> _lastItems = [];
    private List<GridRow> _lastRows = [];
    private IDataTemplate? _rowTemplate;
    private IDataTemplate? _cachedItemTemplate;
    private IDataTemplate? _cachedHeaderTemplate;

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
        HeaderTemplateProperty.Changed.AddClassHandler<VirtualizingFileGrid>((g, _) =>
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

    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
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

        if (_rowTemplate is not null
            && ReferenceEquals(_cachedItemTemplate, ItemTemplate)
            && ReferenceEquals(_cachedHeaderTemplate, HeaderTemplate))
        {
            _rows.ItemTemplate = _rowTemplate;
            return;
        }

        _cachedItemTemplate = ItemTemplate;
        _cachedHeaderTemplate = HeaderTemplate;
        var itemTemplate = ItemTemplate;
        var headerTemplate = HeaderTemplate;
        _rowTemplate = new FuncDataTemplate<GridRow>((row, _) =>
        {
            // One container serves both row kinds: a header band swaps in the (stretched) header
            // content, a tile row swaps in the horizontal stack. Swapping inside a single container
            // keeps ListBox recycling working across mixed rows.
            var host = new Panel { HorizontalAlignment = HorizontalAlignment.Stretch };
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            Control? headerContent = null;

            void UpdateStackChildren(StackPanel s, GridRow? gridRow)
            {
                if (gridRow?.Items is null)
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
                        var content = itemTemplate.Build(items[i]);
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

            void UpdateHost(GridRow? gridRow)
            {
                if (gridRow?.Header is { } header)
                {
                    UpdateStackChildren(stack, null);
                    headerContent ??= headerTemplate?.Build(header);
                    if (headerContent is null)
                    {
                        host.Children.Clear();
                        return;
                    }

                    headerContent.DataContext = header;
                    headerContent.HorizontalAlignment = HorizontalAlignment.Stretch;
                    if (!ReferenceEquals(host.Children.FirstOrDefault(), headerContent))
                    {
                        host.Children.Clear();
                        host.Children.Add(headerContent);
                    }

                    return;
                }

                if (headerContent is not null)
                    headerContent.DataContext = null;

                UpdateStackChildren(stack, gridRow);
                if (!ReferenceEquals(host.Children.FirstOrDefault(), stack))
                {
                    host.Children.Clear();
                    host.Children.Add(stack);
                }
            }

            host.DataContextChanged += (sender, e) =>
            {
                if (sender is Panel p)
                {
                    UpdateHost(p.DataContext as GridRow);
                }
            };

            host.DetachedFromVisualTree += (sender, e) =>
            {
                if (headerContent is not null)
                    headerContent.DataContext = null;

                foreach (var child in stack.Children)
                {
                    if (child is Control c)
                    {
                        c.DataContext = null;
                    }
                }
                stack.Children.Clear();
            };

            host.AttachedToVisualTree += (sender, _) =>
            {
                if (sender is Panel p)
                    UpdateHost(p.DataContext as GridRow);
            };

            UpdateHost(row);

            return host;
        });
        _rows.ItemTemplate = _rowTemplate;
    }

    private void RebuildRows()
    {
        if (_rows is null)
            return;

        var viewportWidth = Bounds.Width;
        if (viewportWidth <= 0)
            viewportWidth = 800;
        var columns = GetColumnCount(viewportWidth);

        if (columns != _lastColumnCount && _lastItems.Count > 0 && _lastItems.Count == _lastItemCount)
        {
            var packed = BuildRows(_lastItems, columns);
            _rows.ItemsSource = packed;
            _rows.SelectedItem = null;
            _lastColumnCount = columns;
            _lastRows = packed;
            return;
        }

        var items = ItemsSource?.Cast<object>().ToList() ?? [];
        if (items.Count == 0)
        {
            _rows.ItemsSource = Array.Empty<GridRow>();
            _rows.SelectedItem = null;
            _lastColumnCount = -1;
            _lastItemCount = 0;
            _lastItemsFingerprint = 0;
            _lastItems = [];
            _lastRows = [];
            return;
        }

        var fingerprint = BuildItemsFingerprint(items);
        if (columns == _lastColumnCount
            && items.Count == _lastItemCount
            && fingerprint == _lastItemsFingerprint
            && ItemsMatch(items, _lastItems)
            && _rows.ItemsSource is IList<GridRow>)
            return;

        var rows = BuildRows(items, columns);

        _rows.ItemsSource = rows;
        _rows.SelectedItem = null;
        _lastColumnCount = columns;
        _lastItemCount = items.Count;
        _lastItemsFingerprint = fingerprint;
        _lastItems = items;
        _lastRows = rows;
    }

    /// <summary>
    /// Packs a heterogeneous item list into rows: group headers get their own full-width band and
    /// flush any partially filled tile row, entries pack into uniform columns.
    /// </summary>
    private static List<GridRow> BuildRows(List<object> items, int columns)
    {
        var rows = new List<GridRow>((items.Count + columns - 1) / columns);
        var index = 0;
        while (index < items.Count)
        {
            if (items[index] is GroupHeaderViewModel)
            {
                rows.Add(new GridRow(items, index, 1));
                index++;
                continue;
            }

            var runEnd = index;
            while (runEnd < items.Count && items[runEnd] is not GroupHeaderViewModel)
                runEnd++;

            for (var start = index; start < runEnd; start += columns)
            {
                // Slice view over the shared flat list instead of GetRange, which allocated and copied a
                // new List<object> per row on every rebuild (once per resize / selection refresh).
                rows.Add(new GridRow(items, start, Math.Min(columns, runEnd - start)));
            }

            index = runEnd;
        }

        return rows;
    }

    private static int BuildItemsFingerprint(IReadOnlyList<object> items)
    {
        var hash = new HashCode();
        foreach (var item in items)
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item));
        return hash.ToHashCode();
    }

    private static bool ItemsMatch(IReadOnlyList<object> left, IReadOnlyList<object> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
                return false;
        }

        return true;
    }

    public int GetColumnCount(double viewportWidth)
    {
        if (viewportWidth <= 0)
            viewportWidth = 800;

        return Math.Max(1, (int)(viewportWidth / Math.Max(48, ItemSize + 12)));
    }

    /// <summary>
    /// Directional navigation over the presentation list. Indices address <see cref="ItemsSource"/>,
    /// which may interleave <see cref="GroupHeaderViewModel"/> bands; those are never a landing spot.
    /// Vertical moves use the packed row geometry so collapsed groups and short trailing rows behave.
    /// </summary>
    public bool TryGetAdjacentIndex(int currentIndex, int itemCount, Avalonia.Input.Key direction, out int targetIndex)
    {
        targetIndex = currentIndex;
        if ((uint)currentIndex >= (uint)itemCount)
            return false;

        // The cached layout only applies when it describes the list the caller is indexing into.
        var items = _lastItems.Count == itemCount ? _lastItems : null;

        if (direction is Avalonia.Input.Key.Left or Avalonia.Input.Key.Right)
        {
            var step = direction == Avalonia.Input.Key.Left ? -1 : 1;
            var candidate = currentIndex + step;
            while ((uint)candidate < (uint)itemCount && items?[candidate] is GroupHeaderViewModel)
                candidate += step;

            if ((uint)candidate >= (uint)itemCount)
                return false;

            targetIndex = candidate;
            return true;
        }

        if (direction is not (Avalonia.Input.Key.Up or Avalonia.Input.Key.Down))
            return false;

        if (items is null || _lastRows.Count == 0)
        {
            var columns = GetColumnCount(Bounds.Width);
            var fallback = direction == Avalonia.Input.Key.Up ? currentIndex - columns : currentIndex + columns;
            if ((uint)fallback >= (uint)itemCount)
                return false;

            targetIndex = fallback;
            return true;
        }

        var rowIndex = FindRowIndex(currentIndex);
        if (rowIndex < 0)
            return false;

        var column = currentIndex - _lastRows[rowIndex].Start;
        var rowStep = direction == Avalonia.Input.Key.Up ? -1 : 1;

        for (var next = rowIndex + rowStep; (uint)next < (uint)_lastRows.Count; next += rowStep)
        {
            var row = _lastRows[next];
            if (row.Header is not null)
                continue;

            targetIndex = row.Start + Math.Min(column, row.Count - 1);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Scrolls the row that contains <paramref name="item"/> into view. Uses the cached packing from the
    /// last rebuild; a no-op when that cache is stale, because the pending rebuild will follow anyway.
    /// </summary>
    public void ScrollIntoView(object item)
    {
        if (_rows is null || _lastItems.Count == 0)
            return;

        var itemIndex = -1;
        for (var i = 0; i < _lastItems.Count; i++)
        {
            if (!ReferenceEquals(_lastItems[i], item))
                continue;

            itemIndex = i;
            break;
        }

        if (itemIndex < 0)
            return;

        var rowIndex = FindRowIndex(itemIndex);
        if (rowIndex >= 0)
            _rows.ScrollIntoView(rowIndex);
    }

    private int FindRowIndex(int itemIndex)
    {
        for (var i = 0; i < _lastRows.Count; i++)
        {
            var row = _lastRows[i];
            if (itemIndex >= row.Start && itemIndex < row.Start + row.Count)
                return i;
        }

        return -1;
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

            // Group header bands are not selectable surfaces; keep them out of marquee hit-testing.
            if (row.DataContext is GroupHeaderViewModel)
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

        public int Start => start;

        /// <summary>Non-null for a full-width band row; such rows never contain tiles.</summary>
        public GroupHeaderViewModel? Header =>
            count == 1 ? source[start] as GroupHeaderViewModel : null;

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
