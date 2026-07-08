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
    private bool _showFileExtensions = true;

    public EntryItemViewModel(
        FileSystemEntry entry,
        bool showFileExtensions = true,
        GitFileStatus gitStatus = GitFileStatus.None)
    {
        Entry = entry;
        _showFileExtensions = showFileExtensions;
        GitStatus = gitStatus;
    }

    public FileSystemEntry Entry { get; }

    public string FullPath => Entry.FullPath;

    public EntryItemViewModel IconAppearance => this;

    internal void NotifyFolderColorChanged()
    {
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(IconAppearance));
    }

    public string Name => Entry.Name;
    public string DisplayName => EntryDisplayName.Format(Entry, _showFileExtensions);
    public bool IsDirectory => Entry.IsDirectory;
    public long SizeBytes => Entry.SizeBytes;
    public DateTime ModifiedUtc => Entry.ModifiedUtc;
    public string Extension => Entry.Extension;
    public string TypeLabel => Entry.TypeLabel;

    internal void SetShowFileExtensions(bool showFileExtensions)
    {
        if (_showFileExtensions == showFileExtensions)
            return;

        _showFileExtensions = showFileExtensions;
        OnPropertyChanged(nameof(DisplayName));
    }

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
