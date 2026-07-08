using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;

namespace HelixExplorer.ViewModels;

public sealed partial class SidebarItemViewModel : ObservableObject
{
    public SidebarItemViewModel(
        string title,
        string? path,
        SidebarItemKind kind,
        bool isSectionHeader = false,
        bool isSelected = false,
        KnownFolderKind? knownFolder = null)
    {
        Title = title;
        Path = path;
        Kind = kind;
        IsSectionHeader = isSectionHeader;
        IsSelected = isSelected;
        KnownFolder = knownFolder;
    }

    public string Title { get; }
    public string? Path { get; }

    internal void NotifyFolderColorChanged()
    {
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(ShowsFolderIcon));
    }

    public bool ShowsFolderIcon => Kind is SidebarItemKind.Home or SidebarItemKind.Pinned;

    public SidebarItemKind Kind { get; }
    public KnownFolderKind? KnownFolder { get; }
    public bool IsSectionHeader { get; }
    public bool IsNavigable => !IsSectionHeader && !string.IsNullOrEmpty(Path);

    [ObservableProperty]
    private bool _isSelected;
}

public enum SidebarItemKind
{
    Section,
    Home,
    Pinned,
    Drive,
    Network,
    Stub
}

public static class SidebarFactory
{
    public static ObservableCollection<SidebarItemViewModel> Build(
        IQuickAccessProvider quickAccess,
        IVolumeProvider volumes,
        IReadOnlyList<NetworkLocationInfo>? networkLocations = null,
        string? selectedPath = null)
    {
        var items = new ObservableCollection<SidebarItemViewModel>();

        var homePath = quickAccess.GetPath(KnownFolderKind.Home);
        items.Add(new SidebarItemViewModel(
            "Home",
            homePath,
            SidebarItemKind.Home,
            isSelected: PathsEqual(homePath, selectedPath)));

        items.Add(new SidebarItemViewModel("Pinned", null, SidebarItemKind.Section, isSectionHeader: true));
        foreach (var (kind, path, displayName) in quickAccess.GetPinnedDefaults())
        {
            items.Add(new SidebarItemViewModel(
                displayName,
                path,
                SidebarItemKind.Pinned,
                knownFolder: kind,
                isSelected: PathsEqual(path, selectedPath)));
        }

        items.Add(new SidebarItemViewModel("Drives", null, SidebarItemKind.Section, isSectionHeader: true));
        foreach (var volume in volumes.GetVolumes())
        {
            items.Add(new SidebarItemViewModel(
                volume.DisplayName,
                volume.RootPath,
                SidebarItemKind.Drive,
                isSelected: PathsEqual(volume.RootPath, selectedPath)));
        }

        items.Add(new SidebarItemViewModel("Network", null, SidebarItemKind.Section, isSectionHeader: true));
        if (networkLocations is { Count: > 0 })
        {
            foreach (var location in networkLocations)
            {
                items.Add(new SidebarItemViewModel(
                    location.DisplayName,
                    location.Path,
                    SidebarItemKind.Network,
                    isSelected: PathsEqual(location.Path, selectedPath)));
            }
        }
        else
        {
            items.Add(new SidebarItemViewModel("Network", @"\\", SidebarItemKind.Network));
        }

        items.Add(new SidebarItemViewModel("Cloud", null, SidebarItemKind.Section, isSectionHeader: true));
        items.Add(new SidebarItemViewModel("Tags", null, SidebarItemKind.Section, isSectionHeader: true));

        return items;
    }

    private static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        return string.Equals(
            a.TrimEnd('\\', '/'),
            b.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }
}
