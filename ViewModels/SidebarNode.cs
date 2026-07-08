using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HelixExplorer.ViewModels;

/// <summary>Hierarchical node in the sidebar tree (Pinned, System Drives, Network).</summary>
public sealed partial class SidebarNode : ObservableObject
{
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _fullPath = string.Empty;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasPopulated;
    [ObservableProperty] private string _icon = "📁";

    public ObservableCollection<SidebarNode> Children { get; } = new();

    public bool IsDrive => FullPath.Length >= 2 && FullPath[1] == ':' && FullPath.EndsWith(Path.DirectorySeparatorChar);

    public Func<SidebarNode, CancellationToken, ValueTask>? LoadChildrenAsync { get; init; }

    public async ValueTask EnsurePopulatedAsync(CancellationToken token = default)
    {
        if (HasPopulated || LoadChildrenAsync is null) return;
        IsLoading = true;
        try
        {
            Children.Clear();
            await LoadChildrenAsync(this, token).ConfigureAwait(false);
            HasPopulated = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Seed the sidebar with Pinned favourites, system drives, and network locations.</summary>
    public static IReadOnlyList<SidebarNode> BuildRoots()
    {
        var roots = new List<SidebarNode>(8);

        // Quick-access pinned favourites.
        var pinned = new SidebarNode
        {
            DisplayName = "Pinned",
            FullPath = string.Empty,
            Icon = "📌",
            IsPinned = true,
            IsExpanded = true,
            HasPopulated = true
        };
        pinned.Children.Add(MakeKnownFolder(Environment.SpecialFolder.UserProfile, "🏠"));
        pinned.Children.Add(MakeKnownFolder(Environment.SpecialFolder.MyDocuments, "📄"));
        pinned.Children.Add(MakeKnownFolder(Environment.SpecialFolder.DesktopDirectory, "🖥️"));
        pinned.Children.Add(MakeKnownFolder(Environment.SpecialFolder.MyMusic, "🎵"));
        pinned.Children.Add(MakeKnownFolder(Environment.SpecialFolder.MyPictures, "🖼️"));
        pinned.Children.Add(MakeKnownFolder(Environment.SpecialFolder.MyVideos, "🎬"));
        roots.Add(pinned);

        // System drives.
        var thisPc = new SidebarNode
        {
            DisplayName = "This PC",
            FullPath = string.Empty,
            Icon = "🖥️",
            IsExpanded = true,
            HasPopulated = true
        };
        foreach (var drive in Environment.GetLogicalDrives())
        {
            thisPc.Children.Add(new SidebarNode
            {
                DisplayName = drive,
                FullPath = drive,
                Icon = "💾",
                LoadChildrenAsync = NodeFromDirectory
            });
        }
        roots.Add(thisPc);

        // Network locations — lazily discovered so a slow handshake never blocks startup.
        roots.Add(new SidebarNode
        {
            DisplayName = "Network",
            FullPath = string.Empty,
            Icon = "🌐",
            LoadChildrenAsync = LoadNetworkAsync
        });

        return roots;
    }

    private static SidebarNode MakeKnownFolder(Environment.SpecialFolder folder, string icon)
    {
        var path = Environment.GetFolderPath(folder);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = folder.ToString();
        return new SidebarNode
        {
            DisplayName = name,
            FullPath = path + (path.EndsWith(Path.DirectorySeparatorChar) ? "" : Path.DirectorySeparatorChar.ToString()),
            Icon = icon,
            LoadChildrenAsync = NodeFromDirectory
        };
    }

    private static async ValueTask NodeFromDirectory(SidebarNode parent, CancellationToken token)
    {
        if (string.IsNullOrEmpty(parent.FullPath) || !Directory.Exists(parent.FullPath)) return;
        await Task.Run(() =>
        {
            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = FileAttributes.System
                };
                foreach (var entry in Directory.EnumerateDirectories(parent.FullPath, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    parent.Children.Add(new SidebarNode
                    {
                        DisplayName = Path.GetFileName(entry),
                        FullPath = entry + Path.DirectorySeparatorChar,
                        Icon = "📁",
                        LoadChildrenAsync = NodeFromDirectory
                    });
                }
            }
            catch (Exception) { /* best effort */ }
        }, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Enumerates mapped network drives. A fuller implementation would also enumerate SMB
    /// shares via WNetOpenEnum; that discovery routine belongs behind the top notification
    /// banner described in the UX spec. TODO: live share discovery.
    /// </summary>
    private static async ValueTask LoadNetworkAsync(SidebarNode parent, CancellationToken token)
    {
        await Task.Run(() =>
        {
            try
            {
                var any = false;
                foreach (var drive in DriveInfo.GetDrives())
                {
                    token.ThrowIfCancellationRequested();
                    if (drive.DriveType != DriveType.Network) continue;
                    any = true;
                    parent.Children.Add(new SidebarNode
                    {
                        DisplayName = drive.Name,
                        FullPath = drive.Name,
                        Icon = "🌐",
                        LoadChildrenAsync = NodeFromDirectory
                    });
                }
                if (!any)
                {
                    parent.Children.Add(new SidebarNode { DisplayName = "No network drives", Icon = "➖" });
                }
            }
            catch (Exception) { /* best effort */ }
        }, token).ConfigureAwait(false);
    }
}
