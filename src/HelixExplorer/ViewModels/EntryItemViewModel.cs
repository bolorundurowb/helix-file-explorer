using CommunityToolkit.Mvvm.ComponentModel;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Services;
using Avalonia.Media.Imaging;

namespace HelixExplorer.ViewModels;

/// <summary>View-facing row wrapping a listing entry with optional git status for coloring.</summary>
public sealed partial class EntryItemViewModel : ObservableObject
{
    public EntryItemViewModel(FileSystemEntry entry, GitFileStatus gitStatus = GitFileStatus.None)
    {
        Entry = entry;
        GitStatus = gitStatus;
    }

    public FileSystemEntry Entry { get; }

    public string FullPath => Entry.FullPath;

    internal void NotifyFolderColorChanged() => OnPropertyChanged(nameof(FullPath));
    public string Name => Entry.Name;
    public bool IsDirectory => Entry.IsDirectory;
    public long SizeBytes => Entry.SizeBytes;
    public DateTime ModifiedUtc => Entry.ModifiedUtc;
    public string Extension => Entry.Extension;
    public string TypeLabel => Entry.TypeLabel;

    [ObservableProperty]
    private GitFileStatus _gitStatus;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _entryImage;

    private int _visualGeneration;

    public async Task RefreshVisualAsync(
        FileVisualService visuals,
        int size,
        bool gridView,
        CancellationToken cancellationToken)
    {
        var generation = ++_visualGeneration;
        var preferThumbnail = FileVisualRules.PreferThumbnail(FullPath, IsDirectory, gridView);

        var image = await visuals.GetBitmapAsync(
            FullPath,
            IsDirectory,
            size,
            preferThumbnail,
            cancellationToken).ConfigureAwait(true);

        if (generation != _visualGeneration || cancellationToken.IsCancellationRequested)
            return;

        EntryImage = image;
    }
}
