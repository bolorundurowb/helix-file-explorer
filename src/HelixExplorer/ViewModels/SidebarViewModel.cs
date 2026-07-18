using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

public sealed class SidebarViewModel(
    IQuickAccessProvider quickAccess,
    IVolumeProvider volumes,
    FileVisualService visuals) : ObservableObject
{
    public ObservableCollection<SidebarItemViewModel> Items { get; } = new();

    public void Rebuild(
        IReadOnlyList<string>? pinnedPaths,
        IReadOnlyList<string>? unpinnedPaths,
        IReadOnlyList<NetworkLocationInfo>? networkLocations = null,
        string? selectedPath = null)
    {
        var built = SidebarFactory.Build(
            quickAccess,
            volumes,
            pinnedPaths,
            unpinnedPaths,
            networkLocations,
            selectedPath);

        Items.Clear();
        foreach (var item in built)
            Items.Add(item);

        _ = LoadIconsAsync();
    }

    public async Task LoadIconsAsync()
    {
        foreach (var item in Items)
        {
            if (!item.IsNavigable || string.IsNullOrEmpty(item.Path) || item.UsesVectorIcon)
                continue;

            try
            {
                var icon = await visuals.GetBitmapAsync(
                    item.Path,
                    isDirectory: true,
                    size: 16,
                    preferThumbnail: false,
                    CancellationToken.None).ConfigureAwait(true);
                item.Icon = icon;
            }
            catch
            {
                item.Icon = null;
            }
        }
    }

    public bool TryPin(string path, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        var normalized = NormalizePinnedPath(path);
        settings.UnpinnedPaths.RemoveAll(p =>
            string.Equals(NormalizePinnedPath(p), normalized, StringComparison.OrdinalIgnoreCase));

        if (!PinnedPathHelper.IsPinned(settings.PinnedPaths, normalized))
            settings.PinnedPaths.Insert(0, normalized);

        return true;
    }

    public bool TryUnpin(string path, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = NormalizePinnedPath(path);
        settings.PinnedPaths.RemoveAll(p =>
            string.Equals(NormalizePinnedPath(p), normalized, StringComparison.OrdinalIgnoreCase));

        var defaults = GetDefaultPinnedPaths();
        if (defaults.Any(d => string.Equals(NormalizePinnedPath(d), normalized, StringComparison.OrdinalIgnoreCase))
            && !settings.UnpinnedPaths.Any(p =>
                string.Equals(NormalizePinnedPath(p), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            settings.UnpinnedPaths.Add(normalized);
        }

        return true;
    }

    public bool CanUnpin(SidebarItemViewModel? item, AppSettings settings)
    {
        if (item is null || !item.IsNavigable || string.IsNullOrEmpty(item.Path))
            return false;

        return PinnedPathHelper.IsVisibleInSidebar(
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            GetDefaultPinnedPaths(),
            item.Path);
    }

    public bool CanPin(SidebarItemViewModel? item, AppSettings settings)
    {
        if (item is null || !item.IsNavigable || string.IsNullOrEmpty(item.Path))
            return false;

        if (item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return false;

        return !PinnedPathHelper.IsPinnedOrDefault(
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            GetDefaultPinnedPaths(),
            item.Path);
    }

    public bool CanPinPath(string? path, AppSettings settings)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return false;

        return !PinnedPathHelper.IsPinnedOrDefault(
            settings.PinnedPaths,
            settings.UnpinnedPaths,
            GetDefaultPinnedPaths(),
            path);
    }

    public void UpdateSelection(string path, bool isHome)
    {
        foreach (var item in Items)
        {
            if (item.IsSectionHeader)
                continue;

            if (item.Kind == SidebarItemKind.Home)
            {
                item.IsSelected = isHome;
                continue;
            }

            item.IsSelected = !string.IsNullOrEmpty(item.Path)
                && string.Equals(
                    item.Path.TrimEnd('\\', '/'),
                    path.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    public void NotifyFolderColorsChanged()
    {
        foreach (var item in Items)
        {
            if (item.IsNavigable)
                item.NotifyFolderColorChanged();
        }
    }

    private IReadOnlyList<string> GetDefaultPinnedPaths()
        => quickAccess.GetPinnedDefaults()
            .Select(t => t.Path)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

    private static string NormalizePinnedPath(string path)
        => path.TrimEnd('\\', '/');
}
