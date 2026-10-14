using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelixExplorer.Controls;
using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.ViewModels;
using CoreDragDropEffects = HelixExplorer.Core.Infrastructure.DragDropEffects;

namespace HelixExplorer.Views;

public sealed partial class Omnibar : UserControl
{
    // A huge folder must not allocate an unbounded name list on the address-bar thread pool.
    private const int MaxSuggestions = 16;
    private const int MaxChildFolders = 200;

    private PaneViewModel? _pane;
    private CancellationTokenSource? _suggestionCts;
    private Flyout? _folderFlyout;
    private int _flyoutGeneration;
    private bool _acceptingSuggestion;

    public Omnibar()
    {
        InitializeComponent();
        SuggestionPopup.PlacementTarget = PathBox;
        // Tunnel runs before window focus navigation, which otherwise treats Tab and Down as focus moves.
        PathBox.AddHandler(KeyDownEvent, OnPathBoxKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Opens the path editor and moves focus into it. Window chrome calls this for Alt+D, Ctrl+L, and F4.
    /// </summary>
    public void BeginEditPath()
    {
        if (_pane is null)
            return;

        _pane.BeginEditPath();
        if (IsHomeToken(_pane.EditablePath))
            _pane.EditablePath = string.Empty;

        Dispatcher.UIThread.Post(FocusPathEditor, DispatcherPriority.Loaded);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_pane is not null)
            _pane.PropertyChanged -= OnPanePropertyChanged;

        CancelSuggestions();
        CloseSuggestions();
        HideFolderFlyout();

        _pane = DataContext as PaneViewModel;
        if (_pane is not null)
        {
            _pane.PropertyChanged += OnPanePropertyChanged;
            RebuildBreadcrumbs();
        }
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaneViewModel.CurrentPath) or nameof(PaneViewModel.Breadcrumbs))
            Dispatcher.UIThread.Post(RebuildBreadcrumbs);
        else if (e.PropertyName == nameof(PaneViewModel.OmnibarMode))
            Dispatcher.UIThread.Post(FocusActiveQueryBox);
        else if (e.PropertyName == nameof(PaneViewModel.IsEditingPath))
            Dispatcher.UIThread.Post(SyncPathEditor, DispatcherPriority.Loaded);
    }

    private void SyncPathEditor()
    {
        if (_pane?.IsEditingPath == true)
        {
            if (IsHomeToken(_pane.EditablePath))
                _pane.EditablePath = string.Empty;
            FocusPathEditor();
            return;
        }

        CancelSuggestions();
        CloseSuggestions();
    }

    private void FocusPathEditor()
    {
        if (_pane?.IsEditingPath != true)
            return;

        PathBox.Focus();
        PathBox.SelectAll();
        QueueSuggestions();
    }

    private void FocusActiveQueryBox()
    {
        if (_pane is null)
            return;

        if (_pane.IsFilterMode)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
        }
        else if (_pane.IsSearchMode)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    private void RebuildBreadcrumbs()
    {
        HideFolderFlyout();
        BreadcrumbHost.Children.Clear();
        if (_pane is null)
            return;

        foreach (var segment in _pane.Breadcrumbs)
        {
            var item = BreadcrumbItem.FromSegment(segment);
            item.SegmentActivated += OnBreadcrumbActivated;
            item.SeparatorActivated += OnBreadcrumbSeparatorActivated;
            item.AddHandler(DragDrop.DragOverEvent, OnBreadcrumbDragOver);
            item.AddHandler(DragDrop.DragLeaveEvent, OnBreadcrumbDragLeave);
            item.AddHandler(DragDrop.DropEvent, OnBreadcrumbDrop);
            BreadcrumbHost.Children.Add(item);
        }
    }

    private void OnBreadcrumbActivated(object? sender, EventArgs e)
    {
        if (sender is BreadcrumbItem item && _pane is not null)
            _pane.NavigateTo(item.SegmentPath);
    }

    private async void OnBreadcrumbSeparatorActivated(object? sender, EventArgs e)
    {
        if (sender is not BreadcrumbItem item)
            return;

        var generation = ++_flyoutGeneration;
        _folderFlyout?.Hide();
        _folderFlyout = null;

        var path = item.SegmentPath;
        IReadOnlyList<ChildFolderRow> rows;
        try
        {
            rows = await Task.Run(() => LoadChildFolders(path)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            rows = [new ChildFolderRow("Can't list this folder", null)];
        }

        if (generation != _flyoutGeneration || !item.IsAttachedToVisualTree())
            return;

        var list = new ListBox
        {
            ItemsSource = rows,
            MaxHeight = 280,
            MinWidth = 180,
            Background = null,
        };
        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = list,
        };
        list.Tapped += (_, args) =>
        {
            if ((args.Source as StyledElement)?.DataContext is ChildFolderRow { FullPath: { } chosen })
            {
                flyout.Hide();
                _pane?.NavigateTo(chosen);
            }
        };
        list.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter
                && list.SelectedItem is ChildFolderRow { FullPath: { } chosen })
            {
                args.Handled = true;
                flyout.Hide();
                _pane?.NavigateTo(chosen);
            }
        };

        _folderFlyout = flyout;
        flyout.ShowAt(item.SeparatorAnchor);
        list.Focus();
    }

    private void OnBreadcrumbAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == BreadcrumbHost || e.Source == BreadcrumbScroll)
            BeginEditPath();
    }

    private void OnPathBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_pane is null || !_pane.IsEditingPath)
            return;

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            CloseSuggestions();
            _pane.CommitEditablePath();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            CloseSuggestions();
            _pane.CancelEditablePath();
            e.Handled = true;
        }
        else if (PathChildCompletion.ShouldAcceptSuggestion(e.Key, e.KeyModifiers))
        {
            var accepted = TryAcceptSuggestion();
            // Down would otherwise move focus out of the address box even when nothing matches.
            if (e.Key == Key.Down || accepted)
                e.Handled = true;
        }
    }

    private void OnPathBoxLostFocus(object? sender, RoutedEventArgs e)
        => Dispatcher.UIThread.Post(FinishPathEditFocus, DispatcherPriority.Input);

    private void FinishPathEditFocus()
    {
        if (_pane is null || !_pane.IsEditingPath || _acceptingSuggestion)
            return;

        if (PathBox.IsKeyboardFocusWithin || SuggestionList.IsKeyboardFocusWithin)
            return;

        CloseSuggestions();
        _pane.CancelEditablePath();
    }

    private void OnPathBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_pane?.IsEditingPath == true)
            QueueSuggestions();
    }

    private void OnSuggestionPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is not PathSuggestion suggestion)
            return;

        e.Handled = true;
        _acceptingSuggestion = true;
        try
        {
            ApplySuggestion(suggestion);
            PathBox.Focus();
        }
        finally
        {
            _acceptingSuggestion = false;
        }
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_pane is null)
            return;

        if (e.Key == Key.Escape)
        {
            _pane.ExitSearchModeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSearchBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_pane is null)
            return;

        // Keep Filter/Search mode while a query is active; exit only when the query is empty.
        if ((_pane.IsFilterMode || _pane.IsSearchMode) && string.IsNullOrWhiteSpace(_pane.FilterText))
            _pane.ExitSearchModeCommand.Execute(null);
    }

    private void OnBreadcrumbDragOver(object? sender, DragEventArgs e)
    {
        if (FindCrumb(e.Source) is not { SegmentPath: var path } || string.IsNullOrEmpty(path))
            return;

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        e.DragEffects = ToPointerEffect(ResolveDrop(e, path));
        e.Handled = true;
    }

    private void OnBreadcrumbDragLeave(object? sender, DragEventArgs e)
        => e.Handled = true;

    private async void OnBreadcrumbDrop(object? sender, DragEventArgs e)
    {
        if (_pane is null || FindCrumb(e.Source) is not { SegmentPath: var path }
            || string.IsNullOrEmpty(path))
            return;

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        var effect = ResolveDrop(e, path);
        e.DragEffects = ToPointerEffect(effect);
        e.Handled = true;

        // HandleDropAsync only copies or moves. A link cursor must not fall through as a move.
        if (effect is not (CoreDragDropEffects.Copy or CoreDragDropEffects.Move))
            return;

        var paths = ReadDroppedPaths(e);
        if (paths.Count == 0)
            return;

        await _pane.HandleDropAsync(paths, path, effect == CoreDragDropEffects.Copy);
    }

    private void QueueSuggestions()
    {
        CancelSuggestions();
        var text = PathBox.Text ?? string.Empty;
        var cts = new CancellationTokenSource();
        _suggestionCts = cts;
        _ = RefreshSuggestionsAsync(text, cts.Token);
    }

    private async Task RefreshSuggestionsAsync(string text, CancellationToken token)
    {
        if (!PathChildCompletion.TryParse(text, out var query))
        {
            if (!token.IsCancellationRequested)
                CloseSuggestions();
            return;
        }

        string[] names;
        try
        {
            names = await Task.Run(() => ListMatchingDirectories(query), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || _pane?.IsEditingPath != true || !PathBox.IsKeyboardFocusWithin)
            return;

        if (names.Length == 0)
        {
            CloseSuggestions();
            return;
        }

        var items = new PathSuggestion[names.Length];
        for (var i = 0; i < names.Length; i++)
            items[i] = new PathSuggestion(names[i], PathChildCompletion.Complete(query, names[i]));

        SuggestionList.ItemsSource = items;
        SuggestionList.SelectedIndex = 0;
        SuggestionPopup.Width = Math.Max(PathBox.Bounds.Width, 180);
        SuggestionPopup.IsOpen = true;
    }

    private bool TryAcceptSuggestion()
    {
        var suggestion = SuggestionList.SelectedItem as PathSuggestion
                         ?? (SuggestionList.Items.Count > 0 ? SuggestionList.Items[0] as PathSuggestion : null);
        if (suggestion is null || !SuggestionPopup.IsOpen)
            return false;

        ApplySuggestion(suggestion);
        return true;
    }

    private void ApplySuggestion(PathSuggestion suggestion)
    {
        if (_pane is null)
            return;

        _pane.EditablePath = suggestion.CompletedPath;
        PathBox.CaretIndex = suggestion.CompletedPath.Length;
        PathBox.SelectionStart = suggestion.CompletedPath.Length;
        PathBox.SelectionEnd = suggestion.CompletedPath.Length;
    }

    private void CloseSuggestions()
    {
        if (SuggestionPopup.IsOpen)
            SuggestionPopup.IsOpen = false;
        SuggestionList.ItemsSource = null;
    }

    private void CancelSuggestions()
    {
        _suggestionCts?.Cancel();
        _suggestionCts?.Dispose();
        _suggestionCts = null;
    }

    private void HideFolderFlyout()
    {
        // Bump the generation so a listing already in flight cannot open over a new path.
        _flyoutGeneration++;
        _folderFlyout?.Hide();
        _folderFlyout = null;
    }

    private static bool IsHomeToken(string? path)
        => string.Equals(path, PaneConstants.HomeRoute, StringComparison.Ordinal);

    private static CoreDragDropEffects ResolveDrop(DragEventArgs e, string destination)
    {
        var modifiers = e.KeyModifiers;
        return DropEffectPolicy.Resolve(
            FirstDroppedPath(e),
            destination,
            modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Shift),
            modifiers.HasFlag(KeyModifiers.Alt));
    }

    private static DragDropEffects ToPointerEffect(CoreDragDropEffects effect) => effect switch
    {
        CoreDragDropEffects.Move => DragDropEffects.Move,
        CoreDragDropEffects.Link => DragDropEffects.Link,
        _ => DragDropEffects.Copy,
    };

    private static string? FirstDroppedPath(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
            return null;

        foreach (var file in files)
        {
            var local = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(local))
                return local;
        }

        return null;
    }

    private static List<string> ReadDroppedPaths(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        var paths = new List<string>(files?.Length ?? 0);
        if (files is null)
            return paths;

        foreach (var file in files)
        {
            var local = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(local))
                paths.Add(local);
        }

        return paths;
    }

    private static string[] ListMatchingDirectories(PathChildCompletion.Query query)
    {
        if (ShellPath.IsShellPath(query.Directory) || ArchivePath.IsVirtual(query.Directory))
            return [];

        try
        {
            if (!Directory.Exists(query.Directory))
                return [];

            return PathChildCompletion.TakeMatches(
                    Directory.EnumerateDirectories(query.Directory).Select(DirectoryName),
                    query.Prefix,
                    MaxSuggestions)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    private static string DirectoryName(string path)
        => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;

    private static IReadOnlyList<ChildFolderRow> LoadChildFolders(string path)
    {
        if (string.IsNullOrEmpty(path) || ShellPath.IsShellPath(path) || ArchivePath.IsVirtual(path))
            return [new ChildFolderRow("No subfolders", null)];

        try
        {
            if (!Directory.Exists(path))
                return [new ChildFolderRow("No subfolders", null)];

            var rows = new List<ChildFolderRow>(32);
            foreach (var child in Directory.EnumerateDirectories(path))
            {
                var name = DirectoryName(child);
                if (name.Length == 0)
                    continue;

                rows.Add(new ChildFolderRow(name, child));
                if (rows.Count >= MaxChildFolders)
                    break;
            }

            if (rows.Count == 0)
                return [new ChildFolderRow("No subfolders", null)];

            rows.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            return rows;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [new ChildFolderRow("Can't list this folder", null)];
        }
    }

    private static BreadcrumbItem? FindCrumb(object? source)
    {
        var current = source as Visual;
        while (current is not null)
        {
            if (current is BreadcrumbItem item)
                return item;
            current = current.GetVisualParent();
        }

        return null;
    }
}

internal static class PathChildCompletion
{
    public readonly record struct Query(string Directory, string Prefix);

    public static bool ShouldAcceptSuggestion(Key key, KeyModifiers modifiers)
        => modifiers == KeyModifiers.None && key is Key.Tab or Key.Down;

    public static bool TryParse(string? text, out Query query)
    {
        query = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (IsUnlistable(trimmed))
            return false;

        var normalized = trimmed.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (NetworkPath.IsUnc(normalized))
            return TryParseUnc(normalized, out query);

        if (IsDrivePrefix(normalized))
        {
            query = new Query(normalized + Path.DirectorySeparatorChar, string.Empty);
            return true;
        }

        if (normalized.EndsWith(Path.DirectorySeparatorChar))
        {
            var directory = normalized.TrimEnd(Path.DirectorySeparatorChar);
            if (IsDrivePrefix(directory))
                directory += Path.DirectorySeparatorChar;
            if (!Path.IsPathRooted(directory))
                return false;

            query = new Query(directory, string.Empty);
            return true;
        }

        var split = normalized.LastIndexOf(Path.DirectorySeparatorChar);
        if (split < 0)
            return false;

        var parent = normalized[..split];
        var prefix = normalized[(split + 1)..];
        if (IsDrivePrefix(parent))
            parent += Path.DirectorySeparatorChar;
        if (!Path.IsPathRooted(parent))
            return false;

        query = new Query(parent, prefix);
        return true;
    }

    public static string Complete(Query query, string childName)
    {
        var directory = query.Directory;
        if (!directory.EndsWith(Path.DirectorySeparatorChar))
            directory += Path.DirectorySeparatorChar;

        return directory + childName + Path.DirectorySeparatorChar;
    }

    public static bool NameMatches(string childName, string prefix)
        => prefix.Length == 0
           || childName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    public static List<string> TakeMatches(IEnumerable<string> names, string prefix, int limit)
    {
        var matches = new List<string>();
        if (limit <= 0)
            return matches;

        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name) || !NameMatches(name, prefix))
                continue;

            matches.Add(name);
            if (matches.Count >= limit)
                break;
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches;
    }

    private static bool TryParseUnc(string normalized, out Query query)
    {
        query = default;
        if (NetworkPath.IsNetworkRoot(normalized))
            return false;

        var body = normalized[2..];
        if (body.EndsWith(Path.DirectorySeparatorChar))
        {
            var directory = @"\\" + body.TrimEnd(Path.DirectorySeparatorChar);
            if (directory.Length <= 2)
                return false;

            query = new Query(directory, string.Empty);
            return true;
        }

        var split = body.LastIndexOf(Path.DirectorySeparatorChar);
        if (split < 0)
        {
            if (body.Length == 0)
                return false;

            query = new Query(@"\\" + body, string.Empty);
            return true;
        }

        var parent = @"\\" + body[..split];
        var prefix = body[(split + 1)..];
        if (parent.Length <= 2)
            return false;

        query = new Query(parent, prefix);
        return true;
    }

    private static bool IsUnlistable(string path)
        => ShellPath.IsShellPath(path)
           || ArchivePath.IsVirtual(path)
           || string.Equals(path, PaneConstants.HomeRoute, StringComparison.Ordinal);

    private static bool IsDrivePrefix(string value)
        => value.Length == 2
           && char.IsLetter(value[0])
           && value[1] == ':';
}

internal sealed record PathSuggestion(string Name, string CompletedPath)
{
    public override string ToString() => Name;
}

internal sealed record ChildFolderRow(string Name, string? FullPath)
{
    public override string ToString() => Name;
}
