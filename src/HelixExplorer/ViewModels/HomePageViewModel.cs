using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Formatting;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

public sealed partial class HomePageViewModel : ObservableObject, IDisposable
{
    private readonly IQuickAccessProvider _quickAccess;
    private readonly IVolumeProvider _volumes;
    private readonly ISettingsStore _settingsStore;
    private readonly IMessenger _messenger;
    private readonly FileVisualService? _visuals;
    private readonly IFolderIconService? _folderIcons;
    private readonly IFolderColorService? _folderColors;

    public HomePageViewModel(
        IQuickAccessProvider quickAccess,
        IVolumeProvider volumes,
        ISettingsStore settingsStore,
        IMessenger messenger,
        FileVisualService? visuals = null,
        IFolderIconService? folderIcons = null,
        IFolderColorService? folderColors = null)
    {
        _quickAccess = quickAccess;
        _volumes = volumes;
        _settingsStore = settingsStore;
        _messenger = messenger;
        _visuals = visuals;
        _folderIcons = folderIcons;
        _folderColors = folderColors;
        if (_folderIcons is not null)
            _folderIcons.IconsChanged += OnFolderIconsChanged;
        if (_folderColors is not null)
            _folderColors.ColorsChanged += OnFolderColorsChanged;
        RefreshPins();
        RefreshDrives();
    }

    public void Dispose()
    {
        if (_folderIcons is not null)
            _folderIcons.IconsChanged -= OnFolderIconsChanged;
        if (_folderColors is not null)
            _folderColors.ColorsChanged -= OnFolderColorsChanged;
    }

    private void OnFolderIconsChanged(object? sender, string path)
        => _ = LoadIconsAsync();

    private void OnFolderColorsChanged(object? sender, EventArgs e)
        => NotifyFolderColorsChanged();

    private void NotifyFolderColorsChanged()
    {
        foreach (var item in QuickAccess)
            item.NotifyFolderColorChanged();
    }

    public ObservableCollection<HomeQuickAccessItem> QuickAccess { get; } = new();
    public ObservableCollection<HomeDriveItem> Drives { get; } = new();
    public ObservableCollection<HomeNetworkItem> NetworkLocations { get; } = new();
    public ObservableCollection<HomeRecentItem> RecentFiles { get; } = new();

    public bool HasNetworkLocations => NetworkLocations.Count > 0;
    public bool HasRecentFiles => RecentFiles.Count > 0;

    public void RequestNavigate(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            _messenger.Send(new GlobalNavigationRequestMessage(path));
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
            QuickAccess.Add(new HomeQuickAccessItem("Home", homePath, isPinned: true));

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
            QuickAccess.Add(new HomeQuickAccessItem(title, path, isPinned: true));
        }

        _ = LoadIconsAsync();
    }

    private async Task LoadIconsAsync()
    {
        if (_visuals is null || _folderIcons is null)
            return;

        foreach (var item in QuickAccess)
        {
            if (string.IsNullOrEmpty(item.Path)
                || !_folderIcons.TryGetIconFile(item.Path, out var iconFile)
                || string.IsNullOrEmpty(iconFile))
            {
                item.Icon = null;
                continue;
            }

            try
            {
                var bitmap = await _visuals.GetBitmapAsync(
                    item.Path,
                    isDirectory: true,
                    size: 40,
                    preferThumbnail: false,
                    CancellationToken.None).ConfigureAwait(true);
                item.Icon = bitmap;
            }
            catch
            {
                item.Icon = null;
            }
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

public sealed partial class HomeQuickAccessItem : ObservableObject
{
    public HomeQuickAccessItem(string title, string path, bool isPinned)
    {
        Title = title;
        Path = path;
        IsPinned = isPinned;
    }

    public string Title { get; }
    public string Path { get; }
    public bool IsPinned { get; }

    internal void NotifyFolderColorChanged() => OnPropertyChanged(nameof(Path));

    [ObservableProperty]
    private Bitmap? _icon;
}

public sealed record HomeDriveItem(string Label, string RootPath, string UsageText, double UsedFraction, bool IsReady)
{
    public bool HasUsage => UsedFraction > 0;
}

public sealed record HomeNetworkItem(string Title, string Path);

public sealed record HomeRecentItem(string Name, string FullPath, string DirectoryPath);
