using CommunityToolkit.Mvvm.Messaging;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Settings;

namespace HelixExplorer.ViewModels.Tests;

public sealed class HomePageViewModelTests
{
    [Fact]
    public void RequestNavigate_PublishesGlobalNavigationMessage()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = CreateViewModel(messenger);

        string? received = null;
        messenger.Register<GlobalNavigationRequestMessage>(this, (_, m) => received = m.Path);

        vm.RequestNavigate(@"C:\somewhere");

        received.Must().Be(@"C:\somewhere");
    }

    [Fact]
    public void RequestNavigate_BlankPath_DoesNotPublish()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = CreateViewModel(messenger);

        var published = false;
        messenger.Register<GlobalNavigationRequestMessage>(this, (_, _) => published = true);

        vm.RequestNavigate(null);
        vm.RequestNavigate(string.Empty);
        vm.RequestNavigate("   ");

        published.Must().BeFalse();
    }

    private static HomePageViewModel CreateViewModel(IMessenger messenger)
        => new(
            new StubQuickAccessProvider(),
            new StubVolumeProvider(),
            new StubSettingsStore(),
            messenger);

    private sealed class StubQuickAccessProvider : IQuickAccessProvider
    {
        public string? GetPath(KnownFolderKind folder) => null;

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
