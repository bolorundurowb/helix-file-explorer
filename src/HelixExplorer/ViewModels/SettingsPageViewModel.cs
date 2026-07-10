using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HelixExplorer.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private static readonly Assembly AppAssembly = typeof(SettingsPageViewModel).Assembly;

    public SettingsPageViewModel(MainWindowViewModel main)
    {
        Main = main;
        AppVersion = AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                     ?? AppAssembly.GetName().Version?.ToString(3)
                     ?? "0.0.0";
        CopyrightNotice = AppAssembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                          ?? "Copyright © 2026";

        Sections = new ObservableCollection<SettingsSection>
        {
            new("general", "General", "M3,17V19H9V17H3M3,5V7H13V5H3M13,21V19H21V17H13V15H11V21H13M7,9V11H3V13H7V15H9V9H7M21,13V11H11V13H21M15,9H17V7H21V5H17V3H15V9Z"),
            new("appearance", "Appearance", "M12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2C17.5,2 22,6 22,10.5C22,13.53 19.53,16 16.5,16H14.5C13.94,16 13.5,16.44 13.5,17C13.5,17.28 13.61,17.53 13.79,17.71C14.11,18.03 14.31,18.47 14.31,19C14.31,19.94 13.55,20.71 12.62,20.71C12.5,20.72 12.36,20.72 12.24,20.72M6.5,10A1.5,1.5 0 0,0 5,11.5A1.5,1.5 0 0,0 6.5,13A1.5,1.5 0 0,0 8,11.5A1.5,1.5 0 0,0 6.5,10M9.5,6A1.5,1.5 0 0,0 8,7.5A1.5,1.5 0 0,0 9.5,9A1.5,1.5 0 0,0 11,7.5A1.5,1.5 0 0,0 9.5,6M14.5,6A1.5,1.5 0 0,0 13,7.5A1.5,1.5 0 0,0 14.5,9A1.5,1.5 0 0,0 16,7.5A1.5,1.5 0 0,0 14.5,6M17.5,10A1.5,1.5 0 0,0 16,11.5A1.5,1.5 0 0,0 17.5,13A1.5,1.5 0 0,0 19,11.5A1.5,1.5 0 0,0 17.5,10Z"),
            new("layout", "Layout", "M3,3H9V9H3V3M3,13H9V19H3V13M13,3H19V9H13V3M13,13H19V19H13V13Z"),
            new("files", "Files & folders", "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z"),
            new("about", "About", "M11,9H13V7H11M12,20C7.59,20 4,16.41 4,12C4,7.59 7.59,4 12,4C16.41,4 20,7.59 20,12C20,16.41 16.41,20 12,20M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,17H13V11H11V17Z"),
        };
        _selectedSection = Sections[0];
        _selectedSection.IsSelected = true;
    }

    public MainWindowViewModel Main { get; }

    public string AppVersion { get; }

    public string CopyrightNotice { get; }

    public ObservableCollection<SettingsSection> Sections { get; }

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
}

public sealed partial class SettingsSection : ObservableObject
{
    public SettingsSection(string key, string title, string iconGeometry)
    {
        Key = key;
        Title = title;
        IconGeometry = iconGeometry;
    }

    public string Key { get; }
    public string Title { get; }
    public string IconGeometry { get; }

    [ObservableProperty]
    private bool _isSelected;
}
