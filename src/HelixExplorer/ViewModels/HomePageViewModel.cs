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
    private readonly AppSettingsCoordinator _settings;
    private Task? _refreshTask;

    public HomePageViewModel(
        IQuickAccessProvider quickAccess,
        IVolumeProvider volumes,
        AppSettingsCoordinator settings)
    {
        _quickAccess = quickAccess;
        _volumes = volumes;
        _settings = settings;
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
    public Task RefreshAsync()
    {
        var active = Volatile.Read(ref _refreshTask);
        if (active is not null && !active.IsCompleted)
            return active;

        var created = RefreshCoreAsync();
        Interlocked.Exchange(ref _refreshTask, created);
        return created;
    }

    private async Task RefreshCoreAsync()
    {
        try
        {
            var settings = _settings.Load();
            var pinned = settings.PinnedPaths.ToArray();
            var unpinned = settings.UnpinnedPaths.ToArray();
            var snapshot = await Task.Run(() => BuildSnapshot(pinned, unpinned))
                .ConfigureAwait(true);

            Replace(QuickAccess, snapshot.Pins);
            Replace(Drives, snapshot.Drives);
        }
        catch (Exception)
        {
            // Home is supplemental chrome; a transient drive or known-folder failure must not fail navigation.
        }
    }

    private HomeIoSnapshot BuildSnapshot(
        IReadOnlyList<string> pinnedPaths,
        IReadOnlyList<string> unpinnedPaths)
    {
        var pins = new List<HomeQuickAccessItem>();
        var homePath = _quickAccess.GetPath(KnownFolderKind.Home);
        if (!string.IsNullOrEmpty(homePath))
            pins.Add(new HomeQuickAccessItem("Home", homePath, IsPinned: true));

        var defaultsWithNames = _quickAccess.GetPinnedDefaults();
        var defaults = defaultsWithNames.Select(t => t.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        var merged = PinnedPathHelper.MergeUserPins(pinnedPaths, defaults, unpinnedPaths);
        var known = defaultsWithNames
            .Where(t => !string.IsNullOrEmpty(t.Path))
            .ToDictionary(t => t.Path.TrimEnd('\\', '/'), t => t.DisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, displayName) in merged)
        {
            if (!path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                var title = known.TryGetValue(path.TrimEnd('\\', '/'), out var name) ? name : displayName;
                pins.Add(new HomeQuickAccessItem(title, path, IsPinned: true));
            }
        }

        var drives = new List<HomeDriveItem>();
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

            drives.Add(new HomeDriveItem(volume.DisplayName, volume.RootPath, usage, fraction, volume.IsReady));
        }

        return new HomeIoSnapshot(pins, drives);
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
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

internal sealed record HomeIoSnapshot(
    IReadOnlyList<HomeQuickAccessItem> Pins,
    IReadOnlyList<HomeDriveItem> Drives);

public sealed record HomeQuickAccessItem(string Title, string Path, bool IsPinned);

public sealed record HomeDriveItem(string Label, string RootPath, string UsageText, double UsedFraction, bool IsReady)
{
    public bool HasUsage => UsedFraction > 0;
}

public sealed record HomeNetworkItem(string Title, string Path);

public sealed record HomeRecentItem(string Name, string FullPath, string DirectoryPath);
