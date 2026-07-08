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
    private FileSystemEntry _entry;
    private bool _showFileExtensions = true;

    public EntryItemViewModel(
        FileSystemEntry entry,
        bool showFileExtensions = true,
        GitFileStatus gitStatus = GitFileStatus.None)
    {
        _entry = entry;
        _showFileExtensions = showFileExtensions;
        GitStatus = gitStatus;
    }

    public FileSystemEntry Entry => _entry;

    public string FullPath => _entry.FullPath;

    public EntryItemViewModel IconAppearance => this;

    internal void NotifyFolderColorChanged()
    {
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(IconAppearance));
    }

    public string Name => _entry.Name;
    public string DisplayName => EntryDisplayName.Format(_entry, _showFileExtensions);
    public bool IsDirectory => _entry.IsDirectory;
    public long SizeBytes => _entry.SizeBytes;
    public DateTime ModifiedUtc => _entry.ModifiedUtc;
    public string Extension => _entry.Extension;
    public string TypeLabel => _entry.TypeLabel;

    internal void UpdateEntry(FileSystemEntry entry, bool showFileExtensions, GitFileStatus gitStatus)
    {
        var entryChanged = !_entry.Equals(entry);
        _entry = entry;

        if (_showFileExtensions != showFileExtensions)
            SetShowFileExtensions(showFileExtensions);

        if (GitStatus != gitStatus)
            GitStatus = gitStatus;

        if (!entryChanged)
            return;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirectory));
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(ModifiedUtc));
        OnPropertyChanged(nameof(Extension));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(IconAppearance));
    }

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
    private bool _isCut;

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
