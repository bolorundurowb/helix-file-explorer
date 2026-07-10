using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Formatting;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.ViewModels;

public sealed partial class HomePageViewModel : ObservableObject
{
    private readonly IQuickAccessProvider _quickAccess;
    private readonly IVolumeProvider _volumes;
    private readonly ISettingsStore _settingsStore;

    public HomePageViewModel(
        IQuickAccessProvider quickAccess,
        IVolumeProvider volumes,
        ISettingsStore settingsStore)
    {
        _quickAccess = quickAccess;
        _volumes = volumes;
        _settingsStore = settingsStore;
        RefreshPins();
        RefreshDrives();
    }

    public event EventHandler<string>? NavigateRequested;

    public ObservableCollection<HomeQuickAccessItem> QuickAccess { get; } = new();
    public ObservableCollection<HomeDriveItem> Drives { get; } = new();
    public ObservableCollection<HomeNetworkItem> NetworkLocations { get; } = new();
    public ObservableCollection<HomeRecentItem> RecentFiles { get; } = new();

    public bool HasNetworkLocations => NetworkLocations.Count > 0;
    public bool HasRecentFiles => RecentFiles.Count > 0;

    public void RequestNavigate(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            NavigateRequested?.Invoke(this, path);
    }

    [RelayCommand]
    private void OpenItem(string? path) => RequestNavigate(path);

    [RelayCommand]
    private void Refresh()
    {
        RefreshPins();
        RefreshDrives();
    }

    public void RefreshPins()
    {
        QuickAccess.Clear();

        var homePath = _quickAccess.GetPath(KnownFolderKind.Home);
        if (!string.IsNullOrEmpty(homePath))
            QuickAccess.Add(new HomeQuickAccessItem("Home", homePath, IsPinned: true));

        var settings = _settingsStore.Load();
        var defaults = _quickAccess.GetPinnedDefaults()
            .Select(t => t.Path)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        var merged = PinnedPathHelper.MergeUserPins(
            settings.PinnedPaths,
            defaults,
            settings.UnpinnedPaths);

        var known = _quickAccess.GetPinnedDefaults()
            .Where(t => !string.IsNullOrEmpty(t.Path))
            .ToDictionary(t => t.Path.TrimEnd('\\', '/'), t => t.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var (path, displayName) in merged)
        {
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                continue;

            var title = known.TryGetValue(path.TrimEnd('\\', '/'), out var name) ? name : displayName;
            QuickAccess.Add(new HomeQuickAccessItem(title, path, IsPinned: true));
        }
    }

    public void RefreshDrives()
    {
        Drives.Clear();
        foreach (var volume in _volumes.GetVolumes())
        {
            string usage;
            double fraction = 0;
            if (volume.IsReady && volume.TotalBytes > 0)
            {
                var free = FileSizeFormatter.FormatBinary(volume.FreeBytes);
                var total = FileSizeFormatter.FormatBinary(volume.TotalBytes);
                usage = $"{free} free of {total}";
                fraction = Math.Clamp(
                    (volume.TotalBytes - volume.FreeBytes) / (double)volume.TotalBytes,
                    0,
                    1);
            }
            else
            {
                usage = volume.IsReady ? string.Empty : "Not ready";
            }

            Drives.Add(new HomeDriveItem(volume.DisplayName, volume.RootPath, usage, fraction, volume.IsReady));
        }
    }

    public void SetNetworkLocations(IReadOnlyList<NetworkLocationInfo> locations)
    {
        NetworkLocations.Clear();
        foreach (var location in locations)
            NetworkLocations.Add(new HomeNetworkItem(location.DisplayName, location.Path));

        OnPropertyChanged(nameof(HasNetworkLocations));
    }

    public void SetRecentFiles(IEnumerable<string> paths)
    {
        RecentFiles.Clear();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name))
                name = path;

            var parent = Path.GetDirectoryName(path) ?? path;
            RecentFiles.Add(new HomeRecentItem(name, path, parent));
        }

        OnPropertyChanged(nameof(HasRecentFiles));
    }
}

public sealed record HomeQuickAccessItem(string Title, string Path, bool IsPinned);

public sealed record HomeDriveItem(string Label, string RootPath, string UsageText, double UsedFraction, bool IsReady)
{
    public bool HasUsage => UsedFraction > 0;
}

public sealed record HomeNetworkItem(string Title, string Path);

public sealed record HomeRecentItem(string Name, string FullPath, string DirectoryPath);
