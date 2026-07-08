using CommunityToolkit.Mvvm.ComponentModel;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private string _title = "Helix Explorer";

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private int _selectedCount;

    public MainWindowViewModel(ISettingsStore settingsStore, IThemeService themeService)
    {
        _settingsStore = settingsStore;
        _themeService = themeService;

        var settings = _settingsStore.Load();
        IsSidebarOpen = settings.SidebarOpen;
    }
}
