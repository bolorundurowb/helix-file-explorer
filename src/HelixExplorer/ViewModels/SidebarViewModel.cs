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

    /// <summary>
    /// Bumped by every <see cref="Rebuild"/>. Lets an in-flight <see cref="LoadIconsAsync"/> from a
    /// superseded rebuild notice it is stale and stop, instead of racing a later call's Clear/Add
    /// against its own enumeration of <see cref="Items"/> or writing icons nobody asked for anymore.
    /// </summary>
    private int _iconLoadGeneration;

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

        // Bumped here as well as in LoadIconsAsync so an in-flight load cannot write an icon into
        // an item this rebuild is about to drop.
        _iconLoadGeneration++;

        // The outgoing items hold counted references from GetBitmapAsync; dropping them without
        // releasing would pin every sidebar icon in the visual cache for the process lifetime.
        foreach (var outgoing in Items)
        {
            var icon = outgoing.Icon;
            outgoing.Icon = null;
            visuals.Release(icon);
        }

        Items.Clear();
        foreach (var item in built)
            Items.Add(item);

        _ = LoadIconsAsync();
    }

    public async Task LoadIconsAsync()
    {
        var generation = ++_iconLoadGeneration;
        // Snapshot before awaiting anything: a later Rebuild() can Clear()/Add() into Items while
        // this call is suspended on GetBitmapAsync, and enumerating the live collection concurrently
        // with that mutation throws.
        var snapshot = Items.ToArray();

        foreach (var item in snapshot)
        {
            if (generation != _iconLoadGeneration)
                return;

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

                // GetBitmapAsync hands back a counted reference, so every path that drops the
                // bitmap has to release it or FileVisualService can never evict it.
                if (generation != _iconLoadGeneration)
                {
                    visuals.Release(icon);
                    return;
                }

                var previous = item.Icon;
                item.Icon = icon;
                visuals.Release(previous);
            }
            catch
            {
                if (generation == _iconLoadGeneration)
                {
                    var previous = item.Icon;
                    item.Icon = null;
                    visuals.Release(previous);
                }
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
