using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using HelixExplorer.Converters;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Theming;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;

namespace HelixExplorer.ViewModels.Tests;

public sealed class ThemeRevisionNameBrushTests
{
    [Fact]
    public void System_reapply_increments_revision_while_requested_variant_stays_default()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var theme = new AvaloniaThemeService();
            var previous = Application.Current!.RequestedThemeVariant;
            try
            {
                theme.ApplyTheme(ThemeMode.System);
                var revision = theme.ThemeRevision;
                Application.Current.RequestedThemeVariant.Must().Be(ThemeVariant.Default);

                theme.ApplyTheme(ThemeMode.System);

                theme.CurrentMode.Must().Be(ThemeMode.System);
                theme.ThemeRevision.Must().Be(revision + 1);
                Application.Current.RequestedThemeVariant.Must().Be(ThemeVariant.Default);
                Application.Current.Resources[AvaloniaThemeService.ThemeRevisionResourceKey]
                    .Must().Be(theme.ThemeRevision);
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previous;
            }
        });
    }

    [Fact]
    public void Name_without_git_status_follows_entry_foreground_when_same_theme_is_reapplied()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var theme = new AvaloniaThemeService();
            var previousRequested = Application.Current!.RequestedThemeVariant;
            object? previousBrush = null;
            ResourceDictionary? dictionary = null;
            Window? window = null;
            try
            {
                theme.ApplyTheme(ThemeMode.Light);
                dictionary = ThemeDictionary(ThemeVariant.Light);
                previousBrush = dictionary["HelixEntryForegroundBrush"];

                var name = ShowName(GitFileStatus.None, out window);
                ForegroundColor(name).Must().Be(Color.Parse("#1A1A1A"));

                var updated = new SolidColorBrush(Color.Parse("#FF00AA"));
                dictionary["HelixEntryForegroundBrush"] = updated;

                // Same explicit mode: ActualThemeVariant does not change. The revision must.
                theme.ApplyTheme(ThemeMode.Light);

                ForegroundColor(name).Must().Be(updated.Color);
            }
            finally
            {
                window?.Close();
                if (dictionary is not null && previousBrush is not null)
                    dictionary["HelixEntryForegroundBrush"] = previousBrush;
                Application.Current.RequestedThemeVariant = previousRequested;
            }
        });
    }

    [Fact]
    public void System_reapply_re_resolves_git_name_brush()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var theme = new AvaloniaThemeService();
            var previousRequested = Application.Current!.RequestedThemeVariant;
            object? previousBrush = null;
            ResourceDictionary? dictionary = null;
            Window? window = null;
            try
            {
                theme.ApplyTheme(ThemeMode.System);
                var variant = AvaloniaThemeService.EffectiveVariant;
                dictionary = ThemeDictionary(variant);
                previousBrush = dictionary["HelixGitModifiedBrush"];

                var name = ShowName(GitFileStatus.Modified, out window);
                var updated = new SolidColorBrush(Color.Parse("#123456"));
                dictionary["HelixGitModifiedBrush"] = updated;
                var before = theme.ThemeRevision;

                theme.ApplyTheme(ThemeMode.System);

                theme.ThemeRevision.Must().Be(before + 1);
                Application.Current.RequestedThemeVariant.Must().Be(ThemeVariant.Default);
                ForegroundColor(name).Must().Be(updated.Color);
            }
            finally
            {
                window?.Close();
                if (dictionary is not null && previousBrush is not null)
                    dictionary["HelixGitModifiedBrush"] = previousBrush;
                Application.Current.RequestedThemeVariant = previousRequested;
            }
        });
    }

    private static TextBlock ShowName(GitFileStatus status, out Window window)
    {
        var entry = new FileSystemEntry(@"C:\theme\a.txt", "a.txt", false, 1, DateTime.UnixEpoch, ".txt");
        var name = new TextBlock
        {
            DataContext = new EntryItemViewModel(entry, gitStatus: status),
            [!TextBlock.ForegroundProperty] = new MultiBinding
            {
                Converter = GitFileStatusBrushConverter.Instance,
                Bindings =
                {
                    new Binding(nameof(EntryItemViewModel.GitStatus)),
                    new DynamicResourceExtension(AvaloniaThemeService.ThemeRevisionResourceKey),
                },
            },
        };

        window = new Window { Content = name, Width = 200, Height = 80 };
        window.Show();
        return name;
    }

    private static ResourceDictionary ThemeDictionary(ThemeVariant variant)
    {
        var found = Application.Current!.Resources.ThemeDictionaries.TryGetValue(variant, out var provider);
        found.Must().BeTrue();
        var dictionary = provider as ResourceDictionary;
        dictionary.Must().NotBeNull();
        return dictionary!;
    }

    private static Color ForegroundColor(TextBlock name)
    {
        var brush = name.Foreground as SolidColorBrush;
        brush.Must().NotBeNull();
        return brush!.Color;
    }
}
