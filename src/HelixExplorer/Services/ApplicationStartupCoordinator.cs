using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using HelixExplorer.ViewModels;
using HelixExplorer.Windows.Theming;

namespace HelixExplorer.Services;

/// <summary>
/// Encapsulates startup wiring that used to live in <see cref="App.axaml.cs"/>:
/// theme/accent application, resource-converter initialization, and the Win32 theme watcher.
/// Keeping this in a dedicated service makes the application bootstrap layer thin and testable.
/// </summary>
public sealed class ApplicationStartupCoordinator(
    ISettingsStore settingsStore,
    IThemeService themeService,
    IUiFontService uiFontService,
    IAccentBrushService accentBrushes,
    IFolderColorService folderColors,
    WinThemeWatcher themeWatcher)
    : IDisposable
{
    private readonly WinThemeWatcher _themeWatcher = themeWatcher;

    public void Initialize(Avalonia.Application application, MainWindowViewModel mainWindowViewModel)
    {
        var settings = settingsStore.Load();

        themeService.ApplyTheme(settings.Theme);
        uiFontService.ApplyFont(settings.UiFont);
        accentBrushes.ApplyCustomAccent(settings.AccentColorArgb);
        themeService.ThemeChanged += _ => accentBrushes.ApplyCustomAccent(accentBrushes.CustomAccentArgb);

        WireResourceConverters(application, settings);
        WireMainWindowViewModel(mainWindowViewModel);
    }

    private void WireResourceConverters(Avalonia.Application application, Core.Settings.AppSettings settings)
    {
        if (application.Resources.TryGetResource("FileSizeConverter", application.ActualThemeVariant, out var converterObj)
            && converterObj is Converters.FileSizeConverter converter)
        {
            converter.Mode = settings.SizeDisplay;
        }

        if (application.Resources.TryGetResource("FolderColorConverter", application.ActualThemeVariant, out var folderColorObj)
            && folderColorObj is Converters.FolderColorConverter folderColorConverter)
        {
            folderColorConverter.FolderColors = folderColors;
        }

        if (application.Resources.TryGetResource("FolderIconBrushConverter", application.ActualThemeVariant, out var folderIconBrushObj)
            && folderIconBrushObj is Converters.FolderIconBrushConverter folderIconBrushConverter)
        {
            folderIconBrushConverter.FolderColors = folderColors;
        }

        if (application.Resources.TryGetResource("EntryIconBrushConverter", application.ActualThemeVariant, out var iconBrushObj)
            && iconBrushObj is Converters.EntryIconBrushConverter iconBrushConverter)
        {
            iconBrushConverter.FolderColors = folderColors;
        }
    }

    private static void WireMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
    {
        if (mainWindowViewModel is null)
            return;

        mainWindowViewModel.SizeDisplayChanged += mode =>
        {
            if (Avalonia.Application.Current?.Resources.TryGetResource(
                    "FileSizeConverter",
                    Avalonia.Application.Current.ActualThemeVariant,
                    out var converterObj) == true
                && converterObj is Converters.FileSizeConverter converter)
            {
                converter.Mode = mode;
            }
        };
    }

    public void Dispose()
        => _themeWatcher.Dispose();
}
