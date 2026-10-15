using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.Core.Theming;
using HelixExplorer.ViewModels;
using HelixExplorer.Views;

namespace HelixExplorer.ViewModels.Tests;

public sealed class SettingsChoiceTests
{
    [Fact]
    public void NullAccent_SelectsOnlyTheDefaultSwatch()
    {
        AccentSwatchSelection.Matches(null, "default").Must().BeTrue();
        AccentSwatchSelection.Matches(null, "#0078D4").Must().BeFalse();
        AccentSwatchSelection.Matches(null, "#107C10").Must().BeFalse();
    }

    [Fact]
    public void StoredAccent_SelectsTheMatchingSwatch()
    {
        var blue = AccentColorDefaults.FromHex("#0078D4");

        AccentSwatchSelection.Matches(blue, "#0078D4").Must().BeTrue();
        AccentSwatchSelection.Matches(blue, "#107C10").Must().BeFalse();
        AccentSwatchSelection.Matches(blue, "default").Must().BeFalse();
        AccentSwatchSelection.Matches(0xFF123456u, "#0078D4").Must().BeFalse();
    }

    [Fact]
    public void CapturedChord_UsesAvaloniaGestureSyntax()
    {
        // Avalonia 12 spells this key Oem3. Parse still accepts the stored default Ctrl+OemTilde.
        TerminalGestureSyntax.TryFormat(Key.OemTilde, KeyModifiers.Control, out var tilde).Must().BeTrue();
        var captured = KeyGesture.Parse(tilde);
        captured.Must().Be(new KeyGesture(Key.OemTilde, KeyModifiers.Control));
        captured.Must().Be(KeyGesture.Parse("Ctrl+OemTilde"));

        TerminalGestureSyntax.TryFormat(Key.T, KeyModifiers.Control | KeyModifiers.Shift, out var chord).Must().BeTrue();
        chord.Must().Be("Ctrl+Shift+T");

        TerminalGestureSyntax.TryFormat(Key.LeftCtrl, KeyModifiers.Control, out _).Must().BeFalse();
        TerminalGestureSyntax.TryFormat(Key.None, KeyModifiers.None, out _).Must().BeFalse();
    }

    [Fact]
    public void BuiltInGesture_Warns_AndTerminalDefaultDoesNot()
    {
        BuiltInGestureConflict.Describe("Ctrl+OemTilde").Must().BeNull();
        BuiltInGestureConflict.Describe("Ctrl+Shift+G").Must().BeNull();
        BuiltInGestureConflict.Describe("ctrl+t").Must().Be("Already used by New tab.");
        BuiltInGestureConflict.Describe("F5").Must().Be("Already used by Refresh.");
        BuiltInGestureConflict.Describe("Escape").Must().Be("Already used by Clear selection.");
        BuiltInGestureConflict.Describe("not a gesture").Must().BeNull();
    }

    [Fact]
    public void GestureCapture_ConsumesChord_BeforeWindowBinding()
    {
        var ran = false;

        HeadlessSession.RunOnUiThread(() =>
        {
            var window = new Window { Width = 400, Height = 300 };
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.T, KeyModifiers.Control),
                Command = new RelayCommand(() => ran = true),
            });

            string? stored = null;
            var capture = new Border { Focusable = true, Width = 180, Height = 32 };
            capture.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
                    return;

                if (!TerminalGestureSyntax.TryFormat(e.Key, e.KeyModifiers, out var gesture))
                    return;

                stored = gesture;
                e.Handled = true;
            }, RoutingStrategies.Tunnel);

            window.Content = capture;
            window.Show();
            try
            {
                window.UpdateLayout();
                capture.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Route = RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                    Source = capture,
                    Key = Key.T,
                    KeyModifiers = KeyModifiers.Control,
                });
                stored.Must().Be("Ctrl+T");
            }
            finally
            {
                window.Close();
            }
        });

        ran.Must().BeFalse();
    }

    [Fact]
    public void SettingsPage_CaptureField_HandlesChord_AndIgnoresModifierKeys()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var page = new SettingsPage();
            var window = new Window { Content = page, Width = 900, Height = 700 };
            window.Show();
            try
            {
                window.UpdateLayout();
                var capture = page.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "TerminalGestureCapture");

                var chord = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Route = RoutingStrategies.Tunnel,
                    Source = capture,
                    Key = Key.T,
                    KeyModifiers = KeyModifiers.Control,
                };
                capture.RaiseEvent(chord);
                chord.Handled.Must().BeTrue();

                var modifier = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Route = RoutingStrategies.Tunnel,
                    Source = capture,
                    Key = Key.LeftCtrl,
                    KeyModifiers = KeyModifiers.Control,
                };
                capture.RaiseEvent(modifier);
                modifier.Handled.Must().BeFalse();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SettingsPage_ShowsChoiceLabels_AccentRing_AndCaptureField()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var page = new SettingsPage();
            var window = new Window { Content = page, Width = 1200, Height = 900 };
            window.Show();
            try
            {
                window.UpdateLayout();

                var labels = page.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .SelectMany(combo => combo.Items.OfType<ComboBoxItem>())
                    .Select(item => item.Content as string)
                    .ToList();

                labels.Must().Contain("Folders first");
                labels.Must().Contain("Files first");
                labels.Must().Contain("Mixed");
                labels.Must().Contain("Columns");
                labels.Must().Contain("Side by side");
                labels.Must().Contain("Stacked");

                var sort = page.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .Single(combo => combo.Items.OfType<ComboBoxItem>().Any(item => item.Content as string == "Folders first"));
                sort.SelectedValue = DirectorySortMode.FilesFirst;
                window.UpdateLayout();
                (sort.SelectedItem as ComboBoxItem)?.Content.Must().Be("Files first");

                var layout = page.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .Single(combo => combo.Items.OfType<ComboBoxItem>().Any(item => item.Content as string == "Columns"));
                layout.SelectedValue = LayoutMode.Miller;
                window.UpdateLayout();
                (layout.SelectedItem as ComboBoxItem)?.Content.Must().Be("Columns");

                var split = page.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .Single(combo => combo.Items.OfType<ComboBoxItem>().Any(item => item.Content as string == "Stacked"));
                split.SelectedValue = PaneSplitOrientation.Horizontal;
                window.UpdateLayout();
                (split.SelectedItem as ComboBoxItem)?.Content.Must().Be("Stacked");

                var text = page.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();
                text.Must().Contain("Accent colour");
                text.Must().Contain("Burnt orange");
                text.Contains("Background colour").Must().BeFalse();

                var capture = page.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "TerminalGestureCapture");
                capture.Focusable.Must().BeTrue();
                page.GetVisualDescendants().OfType<TextBox>().Any(box => box.Name == "TerminalGestureCapture").Must().BeFalse();

                var blue = page.GetVisualDescendants()
                    .OfType<Button>()
                    .Single(button => button.Classes.Contains("accentSwatch") && button.Tag as string == "#0078D4");
                var ring = blue.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("accentRing"));
                var check = blue.GetVisualDescendants().OfType<PathIcon>().Single(icon => icon.Classes.Contains("accentCheck"));

                check.IsVisible.Must().BeFalse();
                IsClear(ring.BorderBrush).Must().BeTrue();

                blue.Classes.Add("selected");
                window.UpdateLayout();

                check.IsVisible.Must().BeTrue();
                IsClear(ring.BorderBrush).Must().BeFalse();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HomePage_RecentSection_UsesFolderTitleAndGlyph()
    {
        var viewModel = new HomePageViewModel(
            new StubQuickAccessProvider(),
            new StubVolumeProvider(),
            new StubSettingsStore(),
            new WeakReferenceMessenger());
        viewModel.SetRecentFiles([@"C:\temp\docs"]);

        HeadlessSession.RunOnUiThread(() =>
        {
            var view = new HomePageView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 800, Height = 700 };
            window.Show();
            try
            {
                window.UpdateLayout();

                var text = view.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();
                text.Must().Contain("Recent folders");
                text.Must().Contain("No recent folders yet.");
                text.Contains("Recent files").Must().BeFalse();

                Application.Current!.TryGetResource("HelixFolderIconGeometry", null, out var folder).Must().BeTrue();
                var recentIcon = view.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Classes.Contains("recentRow"))
                    .SelectMany(button => button.GetVisualDescendants().OfType<PathIcon>())
                    .Single();
                ReferenceEquals(recentIcon.Data, folder).Must().BeTrue();
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static bool IsClear(IBrush? brush)
        => brush is null || brush is ISolidColorBrush { Color.A: 0 };

    private sealed class StubQuickAccessProvider : IQuickAccessProvider
    {
        public string? GetPath(KnownFolderKind folder) => null;

        public IReadOnlyList<(KnownFolderKind Kind, string Path, string DisplayName)> GetPinnedDefaults()
            => [];
    }

    private sealed class StubVolumeProvider : IVolumeProvider
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => [];
    }

    private sealed class StubSettingsStore : ISettingsStore
    {
        public AppSettings Load() => new();

        public void Save(AppSettings settings)
        {
        }

        public void Update(Action<AppSettings> mutate)
        {
        }
    }
}
