using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixExplorer.Core.Theming;

namespace HelixExplorer.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private static readonly Assembly AppAssembly = typeof(SettingsPageViewModel).Assembly;
    private static readonly HttpClient _httpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "HelixExplorer" } }
    };
    private const string ReleasesUrl =
        "https://api.github.com/repos/bolorundurowb/helix-file-explorer/releases/latest";

    public SettingsPageViewModel(MainWindowViewModel main)
    {
        Main = main;
        AppVersion = AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                     ?? AppAssembly.GetName().Version?.ToString(3)
                     ?? "0.0.0";
        CopyrightNotice = AppAssembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                          ?? "Copyright © 2026";

        // Strip any pre-release suffix for comparison (e.g., "1.2.3-beta" -> "1.2.3")
        _currentVersion = AppVersion;
        var plusIndex = _currentVersion.IndexOf('+');
        if (plusIndex >= 0)
            _currentVersion = _currentVersion[..plusIndex];

        Sections = new ObservableCollection<SettingsSection>
        {
            new("general", "General", "M14.95 5a2.5 2.5 0 0 0-4.9 0H2.5a.5.5 0 0 0 0 1h7.55a2.5 2.5 0 0 0 4.9 0h2.55a.5.5 0 0 0 0-1h-2.55ZM12.5 7a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3Zm-2.55 7a2.5 2.5 0 0 0-4.9 0H2.5a.5.5 0 0 0 0 1h2.55a2.5 2.5 0 0 0 4.9 0h7.55a.5.5 0 0 0 0-1H9.95ZM7.5 16a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3Z"),
            new("appearance", "Appearance", "M9.75 6.5a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5Zm3 1a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5Zm2.5 1.5a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Zm-.75 3.75a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5ZM13.25 14a.75.75 0 1 1-1.5 0 .75.75 0 0 1 1.5 0Zm.45-11a7.82 7.82 0 0 0-7.93.17 9.6 9.6 0 0 0-3.25 3.89 5.9 5.9 0 0 0-.62 2.43c0 .8.27 1.57.94 2.12.61.5 1.14.74 1.66.77.51.02.92-.19 1.23-.37l.2-.12c.24-.15.44-.27.69-.35.28-.09.64-.12 1.16.04.19.06.3.14.38.24.09.1.16.26.2.47.06.21.09.46.1.76.02.1.02.24.03.37l.04.58c.05.67.17 1.44.57 2.14.42.7 1.1 1.3 2.2 1.68 1.6.54 3.07.1 4.21-.8a7.46 7.46 0 0 0 2.37-3.6C19.2 9.16 17.68 5.04 13.7 3ZM6.3 4.01a6.82 6.82 0 0 1 6.94-.14c3.5 1.8 4.87 5.4 3.69 9.25a6.46 6.46 0 0 1-2.04 3.1 3.33 3.33 0 0 1-3.26.64c-.9-.3-1.38-.76-1.66-1.24a4 4 0 0 1-.44-1.7l-.04-.54-.02-.41c-.03-.31-.06-.63-.13-.93-.07-.3-.2-.6-.4-.86-.22-.26-.5-.46-.87-.57a2.85 2.85 0 0 0-1.75-.03c-.38.12-.7.32-.95.47l-.14.09c-.29.16-.48.24-.68.23-.22-.01-.55-.12-1.08-.55-.38-.31-.57-.76-.57-1.34 0-.6.19-1.29.52-2.01A8.63 8.63 0 0 1 6.3 4.02Z"),
            new("layout", "Layout", "M7.5 11c.83 0 1.5.67 1.5 1.5v4c0 .83-.67 1.5-1.5 1.5h-4A1.5 1.5 0 0 1 2 16.5v-4c0-.83.67-1.5 1.5-1.5h4Zm9 0c.83 0 1.5.67 1.5 1.5v4c0 .83-.67 1.5-1.5 1.5h-4a1.5 1.5 0 0 1-1.5-1.5v-4c0-.83.67-1.5 1.5-1.5h4Zm-9 1h-4a.5.5 0 0 0-.5.5v4c0 .28.22.5.5.5h4a.5.5 0 0 0 .5-.5v-4a.5.5 0 0 0-.5-.5Zm9 0h-4a.5.5 0 0 0-.5.5v4c0 .28.22.5.5.5h4a.5.5 0 0 0 .5-.5v-4a.5.5 0 0 0-.5-.5Zm-9-10C8.33 2 9 2.67 9 3.5v4C9 8.33 8.33 9 7.5 9h-4A1.5 1.5 0 0 1 2 7.5v-4C2 2.67 2.67 2 3.5 2h4Zm9 0c.83 0 1.5.67 1.5 1.5v4c0 .83-.67 1.5-1.5 1.5h-4A1.5 1.5 0 0 1 11 7.5v-4c0-.83.67-1.5 1.5-1.5h4Zm-9 1h-4a.5.5 0 0 0-.5.5v4c0 .28.22.5.5.5h4a.5.5 0 0 0 .5-.5v-4a.5.5 0 0 0-.5-.5Zm9 0h-4a.5.5 0 0 0-.5.5v4c0 .28.22.5.5.5h4a.5.5 0 0 0 .5-.5v-4a.5.5 0 0 0-.5-.5Z"),
            new("files", "Files & folders", "M4.5 3A2.5 2.5 0 0 0 2 5.5v9A2.5 2.5 0 0 0 4.5 17h11a2.5 2.5 0 0 0 2.5-2.5v-7A2.5 2.5 0 0 0 15.5 5H9.7L8.23 3.51A1.75 1.75 0 0 0 6.98 3H4.5ZM3 5.5C3 4.67 3.67 4 4.5 4h2.48c.2 0 .4.08.53.22L8.8 5.5 7.44 6.85a.5.5 0 0 1-.35.15H3V5.5ZM3 8h4.09c.4 0 .78-.16 1.06-.44L9.7 6h5.79c.83 0 1.5.67 1.5 1.5v7c0 .83-.67 1.5-1.5 1.5h-11A1.5 1.5 0 0 1 3 14.5V8Z"),
            new("about", "About", "M10.5 8.91a.5.5 0 0 0-1 .09v4.6a.5.5 0 0 0 1-.1V8.91Zm.3-2.16a.75.75 0 1 0-1.5 0 .75.75 0 0 0 1.5 0ZM18 10a8 8 0 1 0-16 0 8 8 0 0 0 16 0ZM3 10a7 7 0 1 1 14 0 7 7 0 0 1-14 0Z"),
        };
        _selectedSection = Sections[0];
        _selectedSection.IsSelected = true;
    }

    public MainWindowViewModel Main { get; }

    public string AppVersion { get; }

    public string CopyrightNotice { get; }

    public ObservableCollection<SettingsSection> Sections { get; }

    public IReadOnlyList<UiFontOption> UiFontOptions => UiFontCatalog.Options;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private string? _updateReleaseUrl;

    public bool HasUpdate => !string.IsNullOrEmpty(UpdateReleaseUrl);

    private readonly string _currentVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyPropertyChangedFor(nameof(IsGeneral))]
    [NotifyPropertyChangedFor(nameof(IsAppearance))]
    [NotifyPropertyChangedFor(nameof(IsLayout))]
    [NotifyPropertyChangedFor(nameof(IsFilesFolders))]
    [NotifyPropertyChangedFor(nameof(IsAbout))]
    private SettingsSection _selectedSection;

    public string SelectedTitle => SelectedSection.Title;
    public bool IsGeneral => SelectedSection.Key == "general";
    public bool IsAppearance => SelectedSection.Key == "appearance";
    public bool IsLayout => SelectedSection.Key == "layout";
    public bool IsFilesFolders => SelectedSection.Key == "files";
    public bool IsAbout => SelectedSection.Key == "about";

    [RelayCommand]
    public void SelectSection(SettingsSection? section)
    {
        if (section is null)
            return;

        foreach (var item in Sections)
            item.IsSelected = ReferenceEquals(item, section);

        SelectedSection = section;
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (IsCheckingForUpdates)
            return;

        IsCheckingForUpdates = true;
        UpdateStatus = "Checking for updates...";
        UpdateReleaseUrl = null;

        try
        {
            var response = await _httpClient.GetAsync(ReleasesUrl).ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
            {
                UpdateStatus = "Could not check for updates. Try again later.";
                return;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            using var doc = JsonDocument.Parse(json);
            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();

            if (string.IsNullOrEmpty(tagName))
            {
                UpdateStatus = "No release information found.";
                return;
            }

            var latestVersion = tagName.TrimStart('v', 'V');
            if (IsNewerVersion(latestVersion, _currentVersion))
            {
                UpdateStatus = $"Update available: v{latestVersion} (current: v{_currentVersion})";
                UpdateReleaseUrl = htmlUrl;
            }
            else
            {
                UpdateStatus = $"You are up to date (v{_currentVersion})";
            }
        }
        catch (Exception)
        {
            UpdateStatus = "Could not check for updates. Try again later.";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenReleaseUrl()
    {
        if (UpdateReleaseUrl is { Length: > 0 } url)
            Main.OpenUrl(url);
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (!Version.TryParse(latest, out var latestVer))
            return false;
        if (!Version.TryParse(current, out var currentVer))
            return false;
        return latestVer > currentVer;
    }
}

public sealed partial class SettingsSection(string key, string title, string iconGeometry) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string IconGeometry { get; } = iconGeometry;

    [ObservableProperty]
    private bool _isSelected;
}
