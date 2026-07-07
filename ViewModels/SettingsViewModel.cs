using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels;

/// <summary>Theme + folder-colour + file-size personalisation surface.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;

    public ObservableCollection<FolderColorEntry> FolderColors { get; } = new();
    public SizeDisplayMode[] SizeDisplayModes { get; } = (SizeDisplayMode[])Enum.GetValues(typeof(SizeDisplayMode));

    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private Color _accent;
    [ObservableProperty] private SizeDisplayMode _fileSizeDisplayMode;

    public SettingsViewModel(IThemeService theme, ISettingsService settings)
    {
        _theme = theme;
        _settings = settings;
        IsDarkTheme = _theme.Current == Avalonia.Styling.ThemeVariant.Dark;
        Accent = _theme.Accent;
        FileSizeDisplayMode = _settings.FileSizeDisplayMode;
        foreach (var kv in _theme.FolderColors)
        {
            FolderColors.Add(new FolderColorEntry(kv.Key, kv.Value));
        }
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        _theme.SetTheme(value ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
    }

    partial void OnAccentChanged(Color value) => _theme.SetAccent(value);

    partial void OnFileSizeDisplayModeChanged(SizeDisplayMode value) => _settings.FileSizeDisplayMode = value;

    [RelayCommand]
    private void AddFolderColor(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _theme.SetFolderColor(path, Accent);
        FolderColors.Add(new FolderColorEntry(path, Accent));
    }

    [RelayCommand]
    private void RemoveFolderColor(FolderColorEntry entry)
    {
        if (entry is null) return;
        FolderColors.Remove(entry);
    }

    public sealed class FolderColorEntry
    {
        public string Path { get; }
        public Color Color { get; set; }
        public FolderColorEntry(string path, Color color) { Path = path; Color = color; }
    }
}