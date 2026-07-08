using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.ViewModels;

public sealed partial class SidebarItemViewModel : ObservableObject
{
    public SidebarItemViewModel(
        string title,
        string? path,
        SidebarItemKind kind,
        bool isSectionHeader = false,
        bool isSelected = false,
        KnownFolderKind? knownFolder = null,
        string? toolTip = null)
    {
        Title = title;
        Path = path;
        Kind = kind;
        IsSectionHeader = isSectionHeader;
        IsSelected = isSelected;
        KnownFolder = knownFolder;
        ToolTip = toolTip;
    }

    public string Title { get; }
    public string? Path { get; }
    public string? ToolTip { get; }

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
        IReadOnlyList<string>? userPinnedPaths = null,
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

        var defaultPaths = quickAccess.GetPinnedDefaults()
            .Select(t => t.Path)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        var mergedPins = PinnedPathHelper.MergeUserPins(
            userPinnedPaths ?? Array.Empty<string>(),
            defaultPaths);

        var knownByPath = quickAccess.GetPinnedDefaults()
            .Where(t => !string.IsNullOrEmpty(t.Path))
            .ToDictionary(t => NormalizePath(t.Path!), t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var (path, displayName) in mergedPins)
        {
            KnownFolderKind? knownFolder = null;
            var title = displayName;
            string? toolTip = null;

            if (knownByPath.TryGetValue(NormalizePath(path), out var known))
            {
                knownFolder = known.Kind;
                title = known.DisplayName;
                if (known.Kind == KnownFolderKind.RecycleBin)
                    toolTip = "Opens Recycle Bin in File Explorer";
            }

            items.Add(new SidebarItemViewModel(
                title,
                path,
                SidebarItemKind.Pinned,
                knownFolder: knownFolder,
                isSelected: PathsEqual(path, selectedPath),
                toolTip: toolTip));
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
        return string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
        => path.TrimEnd('\\', '/');
}
