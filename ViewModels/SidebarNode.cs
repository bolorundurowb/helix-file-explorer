using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

/// <summary>Hierarchical node in the sidebar tree (drives, QAT, network locations).</summary>
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

    /// <summary>Seed the sidebar with the host's logical drives.</summary>
    public static IReadOnlyList<SidebarNode> BuildRoots()
    {
        var roots = new List<SidebarNode>(8);

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

        // Quick-access pinned pins.
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

        return roots;
    }

    private static SidebarNode MakeKnownFolder(Environment.SpecialFolder folder, string icon)
    {
        string path = Environment.GetFolderPath(folder);
        string name = Path.GetFileName(path);
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
                        LoadChildrenAsync = NodeFromDirectory
                    });
                }
            }
            catch (Exception) { /* best effort */ }
        }, token).ConfigureAwait(false);
    }
}