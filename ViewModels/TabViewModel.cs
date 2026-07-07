using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    [ObservableProperty] private bool _isDualPane = false;

    /// <summary>Which pane is currently focused (receives omnibar and sidebar navigations).</summary>
    [ObservableProperty] private PaneViewModel _activePane;

    public TabViewModel(PaneViewModel left, PaneViewModel right)
    {
        Left = left;
        Right = right;
        ActivePane = left;

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

    /// <summary>Built on construction with the shared <see cref="Services"/> services.</summary>
    public static TabViewModel Create(string initialPath)
    {
        string path = string.IsNullOrEmpty(initialPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : initialPath;
        if (!path.EndsWith(System.IO.Path.DirectorySeparatorChar))
        {
            path += System.IO.Path.DirectorySeparatorChar;
        }

        var left = NewPane(path);
        var right = NewPane(path);
        return new TabViewModel(left, right);
    }

    private static PaneViewModel NewPane(string path)
    {
        var pane = new PaneViewModel(ServiceLocator.Watcher, ServiceLocator.FileSystem, ServiceLocator.Archive, ServiceLocator.Git);
        pane.NavigateTo(path);
        return pane;
    }

    private void OnPaneNavigated(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ActivePane))
        {
            Title = ActivePane.IsArchive ? "Archive" : System.IO.Path.GetFileName(ActivePane.CurrentPath.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(Title)) Title = ActivePane.CurrentPath;
        }
    }

    private void OnEntryActivated(object? sender, FileSystemEntry e)
    {
        // Default behaviour: launch via the OS shell.
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.FullPath,
                    UseShellExecute = true
                })?.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TabViewModel.OnEntryActivated: {ex.Message}");
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

    public void Dispose()
    {
        Left.Dispose();
        Right.Dispose();
    }
}