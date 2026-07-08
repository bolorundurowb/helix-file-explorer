using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly IThemeService _themeService;
    private readonly IQuickAccessProvider _quickAccess;
    private readonly IVolumeProvider _volumes;
    private bool _disposed;

    public MainWindowViewModel(
        ISettingsStore settingsStore,
        IThemeService themeService,
        IFileSystemProvider fileSystem,
        IQuickAccessProvider quickAccess,
        IVolumeProvider volumes)
    {
        _settingsStore = settingsStore;
        _themeService = themeService;
        _quickAccess = quickAccess;
        _volumes = volumes;

        var settings = _settingsStore.Load();
        IsSidebarOpen = settings.SidebarOpen;

        ActivePane = new PaneViewModel(fileSystem);
        ActivePane.Navigated += OnPaneNavigated;
        ActivePane.PropertyChanged += OnPanePropertyChanged;

        SidebarItems = SidebarFactory.Build(_quickAccess, _volumes);

        var home = _quickAccess.GetPath(Core.Models.KnownFolderKind.Home)
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ActivePane.NavigateTo(home);
    }

    public PaneViewModel ActivePane { get; }

    public ObservableCollection<SidebarItemViewModel> SidebarItems { get; }

    [ObservableProperty]
    private string _title = "Helix Explorer";

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    public int ItemCount => ActivePane.ItemCount;
    public int SelectedCount => ActivePane.SelectedCount;
    public bool IsLoading => ActivePane.IsLoading;
    public string StatusText => ActivePane.StatusText;

    private void OnPaneNavigated(object? sender, EventArgs e)
    {
        UpdateSidebarSelection(ActivePane.CurrentPath);
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(StatusText));
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaneViewModel.ItemCount)
            or nameof(PaneViewModel.SelectedCount)
            or nameof(PaneViewModel.IsLoading)
            or nameof(PaneViewModel.StatusText))
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(PaneViewModel.SelectedCount))
                OnPropertyChanged(nameof(SelectedCount));
            if (e.PropertyName == nameof(PaneViewModel.ItemCount))
                OnPropertyChanged(nameof(ItemCount));
            if (e.PropertyName == nameof(PaneViewModel.IsLoading))
                OnPropertyChanged(nameof(IsLoading));
            if (e.PropertyName == nameof(PaneViewModel.StatusText))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    private void UpdateSidebarSelection(string path)
    {
        foreach (var item in SidebarItems)
        {
            if (item.IsSectionHeader)
                continue;

            item.IsSelected = !string.IsNullOrEmpty(item.Path)
                && string.Equals(
                    item.Path.TrimEnd('\\', '/'),
                    path.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand]
    private void NavigateSidebar(SidebarItemViewModel? item)
    {
        if (item is null || !item.IsNavigable || string.IsNullOrEmpty(item.Path))
            return;

        if (item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return;

        ActivePane.NavigateTo(item.Path);
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
        var settings = _settingsStore.Load();
        settings.SidebarOpen = IsSidebarOpen;
        _settingsStore.Save(settings);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = _themeService.CurrentMode switch
        {
            ThemeMode.Light => ThemeMode.Dark,
            ThemeMode.Dark => ThemeMode.System,
            _ => ThemeMode.Light
        };
        _themeService.ApplyTheme(next);
        var settings = _settingsStore.Load();
        settings.Theme = next;
        _settingsStore.Save(settings);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ActivePane.Navigated -= OnPaneNavigated;
        ActivePane.PropertyChanged -= OnPanePropertyChanged;
        ActivePane.Dispose();
    }
}
