using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;
using HelixExplorer.ViewModels;
using HelixExplorer.Views;

namespace HelixExplorer.ViewModels.Tests;

public sealed class HeadlessUiTests
{
    [Fact]
    public void MainWindow_ShowsHeadlessly()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var window = new MainWindow();
            try
            {
                window.Show();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void HomePage_QuickAccessButton_ExecutesNavigationCommand()
    {
        var messenger = new WeakReferenceMessenger();
        string? received = null;
        messenger.Register<GlobalNavigationRequestMessage>(this, (_, m) => received = m.Path);

        var viewModel = new HomePageViewModel(
            new StubQuickAccessProvider(),
            new StubVolumeProvider(),
            new StubSettingsStore(),
            messenger);

        HeadlessSession.RunOnUiThread(() =>
        {
            var view = new HomePageView { DataContext = viewModel };
            var window = new Window { Content = view, Width = 800, Height = 600 };
            window.Show();

            var button = window.GetVisualDescendants()
                .OfType<Button>()
                .First(b => b.Classes.Contains("homeCard")
                            && string.Equals(b.CommandParameter as string, @"C:\home", StringComparison.OrdinalIgnoreCase));

            button.Command!.Execute(button.CommandParameter);

            window.Close();
        });

        received.Must().Be(@"C:\home");
    }

    [Fact]
    public void KeyBinding_ExecutesCommand_OnKeyPress()
    {
        var viewModel = new ShortcutViewModel();

        HeadlessSession.RunOnUiThread(() =>
        {
            var window = new Window { DataContext = viewModel, Width = 400, Height = 300 };
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.T, KeyModifiers.Control),
                Command = viewModel.IncrementCommand,
            });
            window.Show();

            window.KeyPress(Key.T, RawInputModifiers.Control, PhysicalKey.T, null);

            window.Close();
        });

        viewModel.Count.Must().Be(1);
    }

    private sealed class ShortcutViewModel
    {
        public int Count { get; private set; }

        public IRelayCommand IncrementCommand { get; }

        public ShortcutViewModel() => IncrementCommand = new RelayCommand(() => Count++);
    }

    private sealed class StubQuickAccessProvider : IQuickAccessProvider
    {
        public string? GetPath(KnownFolderKind folder) => folder == KnownFolderKind.Home ? @"C:\home" : null;

        public IReadOnlyList<(KnownFolderKind Kind, string Path, string DisplayName)> GetPinnedDefaults()
            => Array.Empty<(KnownFolderKind, string, string)>();
    }

    private sealed class StubVolumeProvider : IVolumeProvider
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => Array.Empty<VolumeInfo>();
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
