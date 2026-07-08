using CommunityToolkit.Mvvm.ComponentModel;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;

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
    public string Name => Entry.Name;
    public bool IsDirectory => Entry.IsDirectory;
    public long SizeBytes => Entry.SizeBytes;
    public DateTime ModifiedUtc => Entry.ModifiedUtc;
    public string Extension => Entry.Extension;
    public string TypeLabel => Entry.TypeLabel;

    [ObservableProperty]
    private GitFileStatus _gitStatus;
}
