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
    private bool _syncing;

    public ObservableCollection<FolderColorEntry> FolderColors { get; } = new();
    public SizeDisplayMode[] SizeDisplayModes { get; } = (SizeDisplayMode[])Enum.GetValues(typeof(SizeDisplayMode));
    public LayoutMode[] ViewModes { get; } = (LayoutMode[])Enum.GetValues(typeof(LayoutMode));

    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private bool _followSystemTheme;
    [ObservableProperty] private Color _accent;
    [ObservableProperty] private SizeDisplayMode _fileSizeDisplayMode;
    [ObservableProperty] private double _thumbnailSize;
    [ObservableProperty] private LayoutMode _defaultViewMode;
    [ObservableProperty] private bool _dualPaneVertical;

    public SettingsViewModel(IThemeService theme, ISettingsService settings)
    {
        _theme = theme;
        _settings = settings;
        _syncing = true;
        IsDarkTheme = _theme.Current == Avalonia.Styling.ThemeVariant.Dark;
        FollowSystemTheme = _theme.FollowSystemTheme;
        Accent = _theme.Accent;
        FileSizeDisplayMode = _settings.FileSizeDisplayMode;
        ThumbnailSize = _settings.ThumbnailSize;
        DefaultViewMode = (LayoutMode)_settings.DefaultViewMode;
        DualPaneVertical = _settings.DualPaneVertical;
        foreach (var kv in _theme.FolderColors)
        {
            FolderColors.Add(new FolderColorEntry(kv.Key, kv.Value));
        }
        _syncing = false;
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (_syncing) return;
        _theme.SetTheme(value ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        // An explicit theme choice stops following the system.
        _syncing = true;
        FollowSystemTheme = false;
        _syncing = false;
    }

    partial void OnFollowSystemThemeChanged(bool value)
    {
        if (_syncing) return;
        _theme.FollowSystemTheme = value;
    }

    partial void OnAccentChanged(Color value)
    {
        if (_syncing) return;
        _theme.SetAccent(value);
    }

    partial void OnFileSizeDisplayModeChanged(SizeDisplayMode value)
    {
        if (_syncing) return;
        _settings.FileSizeDisplayMode = value;
    }

    partial void OnThumbnailSizeChanged(double value)
    {
        if (_syncing) return;
        _settings.ThumbnailSize = value;
    }

    partial void OnDefaultViewModeChanged(LayoutMode value)
    {
        if (_syncing) return;
        _settings.DefaultViewMode = (int)value;
    }

    partial void OnDualPaneVerticalChanged(bool value)
    {
        if (_syncing) return;
        _settings.DualPaneVertical = value;
    }

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
        _theme.RemoveFolderColor(entry.Path); // keep the theme service in sync
    }

    public sealed class FolderColorEntry
    {
        public string Path { get; }
        public Color Color { get; set; }
        public FolderColorEntry(string path, Color color) { Path = path; Color = color; }
    }
}
