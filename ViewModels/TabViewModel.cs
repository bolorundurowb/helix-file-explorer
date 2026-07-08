using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using HelixExplorer.Models;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

/// <summary>Manages one workspace — a left and right pane plus their status wiring.</summary>
public sealed partial class TabViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _title = "New Tab";

    public PaneViewModel Left { get; }
    public PaneViewModel Right { get; }

    /// <summary>True when the tab is in dual-pane mode, false when collapsed to a single pane.</summary>
    [ObservableProperty] private bool _isDualPane;

    /// <summary>True when the dual-pane split is vertical (top/bottom) rather than side-by-side.</summary>
    [ObservableProperty] private bool _verticalSplit;

    /// <summary>Optional tab tint colour (right-click → colour picker). Null = default.</summary>
    [ObservableProperty] private Avalonia.Media.Color? _tint;

    /// <summary>Which pane is currently focused (receives omnibar and sidebar navigations).</summary>
    [ObservableProperty] private PaneViewModel _activePane;

    public TabViewModel(PaneViewModel left, PaneViewModel right)
    {
        Left = left;
        Right = right;
        _activePane = left;
        _verticalSplit = ServiceLocator.Settings.DualPaneVertical;
        Left.IsActive = true;

        left.Navigated += OnPaneNavigated;
        right.Navigated += OnPaneNavigated;
        left.EntryActivated += OnEntryActivated;
        right.EntryActivated += OnEntryActivated;
        left.OpenInOtherPaneRequested += OnOpenInOtherPane;
        right.OpenInOtherPaneRequested += OnOpenInOtherPane;
    }

    private void OnOpenInOtherPane(object? sender, FileSystemEntry e)
    {
        if (sender is not PaneViewModel source) return;
        var target = ReferenceEquals(source, Left) ? Right : Left;
        IsDualPane = true;
        target.NavigateTo(e.FullPath);
    }

    /// <summary>Builds a tab whose panes start at <paramref name="initialPath"/>.</summary>
    public static TabViewModel Create(string initialPath)
    {
        string path = string.IsNullOrEmpty(initialPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : initialPath;
        if (!path.EndsWith(System.IO.Path.DirectorySeparatorChar))
        {
            path += System.IO.Path.DirectorySeparatorChar;
        }

        return new TabViewModel(NewPane(path), NewPane(path));
    }

    private static PaneViewModel NewPane(string path)
    {
        var pane = new PaneViewModel(ServiceLocator.FileSystem, ServiceLocator.Archive, ServiceLocator.Git);
        pane.NavigateTo(path);
        return pane;
    }

    partial void OnActivePaneChanged(PaneViewModel value)
    {
        Left.IsActive = ReferenceEquals(value, Left);
        Right.IsActive = ReferenceEquals(value, Right);
    }

    private void OnPaneNavigated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ActivePane))
        {
            Title = ActivePane.IsArchive
                ? "Archive"
                : System.IO.Path.GetFileName(ActivePane.CurrentPath.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(Title)) Title = ActivePane.CurrentPath;
        }
    }

    private async void OnEntryActivated(object? sender, FileSystemEntry e)
    {
        try
        {
            string path = e.FullPath;

            // Files inside an archive must be extracted before the OS can open them.
            if (path.StartsWith(ArchiveService.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                var extracted = await ServiceLocator.Archive.ExtractEntryAsync(path).ConfigureAwait(true);
                if (string.IsNullOrEmpty(extracted)) return;
                path = extracted;
            }

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TabViewModel.OnEntryActivated: {ex.Message}");
        }
    }

    public void FocusPane(PaneViewModel pane)
    {
        if (ReferenceEquals(pane, Left) || ReferenceEquals(pane, Right))
        {
            ActivePane = pane;
        }
    }

    public void SwapPanes()
    {
        (Left.CurrentPath, Right.CurrentPath) = (Right.CurrentPath, Left.CurrentPath);
    }

    public void ToggleOrientation() => VerticalSplit = !VerticalSplit;

    // ---- session persistence ----

    public TabState CaptureState() => new()
    {
        Title = Title,
        IsDualPane = IsDualPane,
        VerticalSplit = VerticalSplit,
        ActivePaneIndex = ReferenceEquals(ActivePane, Right) ? 1 : 0,
        Left = Left.CaptureState(),
        Right = Right.CaptureState(),
        TintArgb = Tint?.ToUInt32() ?? 0
    };

    public static TabViewModel Restore(TabState state)
    {
        var tab = Create(state.Left.Path);
        tab.IsDualPane = state.IsDualPane;
        tab.VerticalSplit = state.VerticalSplit;
        tab.Left.RestoreState(state.Left);
        tab.Right.RestoreState(state.Right);
        tab.ActivePane = state.ActivePaneIndex == 1 ? tab.Right : tab.Left;
        if (state.TintArgb != 0) tab.Tint = Avalonia.Media.Color.FromUInt32(state.TintArgb);
        return tab;
    }

    public void Dispose()
    {
        Left.Dispose();
        Right.Dispose();
    }
}
