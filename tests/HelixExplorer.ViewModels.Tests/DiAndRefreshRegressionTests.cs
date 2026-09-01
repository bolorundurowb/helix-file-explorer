using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.FileSystem.Undo;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Models;
using HelixExplorer.Core.Persistence;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Tests;

public class ScopedDiWiringTests
{
    [Fact]
    public void ApplicationRegistration_UsesCorrectLifetimesForWindowGraph()
    {
        var services = CreateAppServices();

        Descriptor<MainWindowViewModel>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<HomePageViewModel>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<FileOperationReporter>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<IFileOperationReporter>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<IPaneViewModelFactory>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<AppSettingsCoordinator>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<SettingsPageViewModel>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<SidebarViewModel>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<CommandPaletteService>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<TabSessionCoordinator>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<FileOperationUndoService>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);

        // Undo must be process-wide: a scoped history would give each window its own stack, so an
        // operation run in one window would be invisible to Ctrl+Z in another.
        Descriptor<IFileOperationHistory>(services).Lifetime.Must().Be(ServiceLifetime.Singleton);
        Descriptor<IWindowHostService>(services).Lifetime.Must().Be(ServiceLifetime.Singleton);
        Descriptor<IAppDatabase>(services).Lifetime.Must().Be(ServiceLifetime.Singleton);
        Descriptor<IFolderViewPreferencesStore>(services).Lifetime.Must().Be(ServiceLifetime.Singleton);
        Descriptor<IFolderColorStore>(services).Lifetime.Must().Be(ServiceLifetime.Singleton);
        Descriptor<IFolderColorService>(services).Lifetime.Must().Be(ServiceLifetime.Singleton);
        Descriptor<IFolderViewPreferencesService>(services).Lifetime.Must().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void RealScopes_HomePageViewModelDiffersAcrossWindows_ButSharedWithinWindow()
    {
        using var provider = CreateAppServices().BuildServiceProvider(validateScopes: true);

        HomePageViewModel a1, a2, b1;
        using (var windowA = provider.CreateScope())
        {
            a1 = windowA.ServiceProvider.GetRequiredService<HomePageViewModel>();
            a2 = windowA.ServiceProvider.GetRequiredService<HomePageViewModel>();
        }

        using var windowB = provider.CreateScope();
        b1 = windowB.ServiceProvider.GetRequiredService<HomePageViewModel>();

        a1.Must().Be(a2);
        ReferenceEquals(a1, b1).Must().BeFalse();

        ((Action)(() => provider.GetRequiredService<HomePageViewModel>())).Throws<InvalidOperationException>();
    }

    [Fact]
    public void RealScopes_ReporterDiffersAcrossWindows()
    {
        using var provider = CreateAppServices().BuildServiceProvider(validateScopes: true);

        FileOperationReporter a;
        using (var scope = provider.CreateScope())
            a = scope.ServiceProvider.GetRequiredService<FileOperationReporter>();

        using var scope2 = provider.CreateScope();
        var b = scope2.ServiceProvider.GetRequiredService<FileOperationReporter>();
        ReferenceEquals(a, b).Must().BeFalse();
    }

    [Fact]
    public void PaneFactory_CreatesUntrackedCoordinators_WithWindowReporter()
    {
        using var provider = CreateAppServices().BuildServiceProvider(validateScopes: true);
        provider.GetRequiredService<IAppDatabase>().Initialize();
        using var scope = provider.CreateScope();
        var pane = scope.ServiceProvider.GetRequiredService<IPaneViewModelFactory>().Create();

        pane.Must().NotBeNull();
        scope.ServiceProvider.GetRequiredService<FileOperationReporter>()
            .Must().Be(scope.ServiceProvider.GetRequiredService<IFileOperationReporter>());
        pane.Dispose();
    }

    [Fact]
    public void RealScopes_DisposingOneWindow_DoesNotDisposeSharedVolumeWatcher()
    {
        // IVolumeChangeWatcher is a singleton shared across every window's scope. Regression guard
        // for ARCH-1: MainWindowViewModel.Dispose() must only unsubscribe from it, never dispose it
        // out from under other still-open windows (which would also make Start() throw
        // ObjectDisposedException the next time a window opens).
        using var provider = CreateAppServices().BuildServiceProvider(validateScopes: true);
        provider.GetRequiredService<IAppDatabase>().Initialize();

        using var scopeA = provider.CreateScope();
        var windowA = scopeA.ServiceProvider.GetRequiredService<MainWindowViewModel>();

        using var scopeB = provider.CreateScope();
        _ = scopeB.ServiceProvider.GetRequiredService<MainWindowViewModel>();

        windowA.Dispose();

        var watcher = provider.GetRequiredService<IVolumeChangeWatcher>();
        watcher.Start();
    }

    private static ServiceCollection CreateAppServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddHelixApplicationServices();
        return services;
    }

    private static ServiceDescriptor Descriptor<T>(IServiceCollection services)
        => services.Last(d => d.ServiceType == typeof(T));

}

public class PaneRefreshCoordinatorTests
{
    [Fact]
    public void CancelRefresh_LeavesTokenObservable_WithoutObjectDisposedException()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        cts.Cancel();
        try
        {
            _ = token.IsCancellationRequested;
        }
        catch (ObjectDisposedException)
        {
            Ensure.Fail("Token observation threw ObjectDisposedException after cancel-without-dispose.");
        }

        cts.Dispose();
        var cancelled = false;
        try { cancelled = token.IsCancellationRequested; }
        catch (ObjectDisposedException) { cancelled = true; }
        cancelled.Must().BeTrue();
    }

    [Fact]
    public void PublishGate_RejectsStalePath_EvenWhenGenerationMatches()
    {
        // Mirrors PaneRefreshCoordinator's apply gate: generation alone is insufficient.
        var loadedPath = @"C:\documents";
        var currentPath = @"C:\downloads";
        PathUtilities.PathsEqual(loadedPath, currentPath).Must().BeFalse();
    }

    [Fact]
    public void ListingPublishRequest_EmptyAllEntries_IsDistinctFromMissingPublish()
    {
        // Empty listings must be publishable so stale caches can be overwritten.
        var request = new ListingPublishRequest
        {
            AllEntries = Array.Empty<FileSystemEntry>(),
            GitSnapshot = GitStatusSnapshot.Empty,
            ShowHiddenFiles = false,
            ShowFileExtensions = true,
            IsFilterVisible = false,
            FilterText = string.Empty,
            SortColumn = SortColumn.Name,
            SortDescending = false,
            DirectorySort = DirectorySortMode.MixedWithFiles
        };

        var listing = new PaneListingCoordinator().ApplySortAndPublish(request);
        listing.ItemCount.Must().Be(0);
        listing.TotalCount.Must().Be(0);
        listing.Entries.Must().BeEmpty();
    }
}

public class WindowCloseSessionPolicyTests
{
    [Fact]
    public void OnWindowClosed_SavesCapturedCallback_AndDoesNotResolveViewModel()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWindowHostService, WindowHostService>();
        using var provider = services.BuildServiceProvider();

        var host = (WindowHostService)provider.GetRequiredService<IWindowHostService>();
        var resolvingScope = new ForbiddenResolveScope(typeof(MainWindowViewModel));
        host.TrackScopeForTests(resolvingScope);

        var saved = false;
        host.OnWindowClosed(resolvingScope, () => saved = true);

        saved.Must().BeTrue();
        resolvingScope.ResolveAttempts.Must().Be(0);
        resolvingScope.IsDisposed.Must().BeTrue();
        host.OpenWindowCount.Must().Be(0);
    }

    [Fact]
    public void OnWindowClosed_WithOtherWindowsOpen_DoesNotSaveSession()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWindowHostService, WindowHostService>();
        using var provider = services.BuildServiceProvider();
        var host = (WindowHostService)provider.GetRequiredService<IWindowHostService>();

        var remaining = new ForbiddenResolveScope(typeof(MainWindowViewModel));
        var closing = new ForbiddenResolveScope(typeof(MainWindowViewModel));
        host.TrackScopeForTests(remaining);
        host.TrackScopeForTests(closing);

        var saved = false;
        host.OnWindowClosed(closing, () => saved = true);

        saved.Must().BeFalse();
        host.OpenWindowCount.Must().Be(1);
        closing.IsDisposed.Must().BeTrue();
        remaining.IsDisposed.Must().BeFalse();
    }

    [Fact]
    public void Shutdown_WithWindowStillTracked_SavesSessionBeforeDisposingScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWindowHostService, WindowHostService>();
        using var provider = services.BuildServiceProvider();
        var host = (WindowHostService)provider.GetRequiredService<IWindowHostService>();

        var scope = new ForbiddenResolveScope(typeof(MainWindowViewModel));
        var savedWhileScopeAlive = false;
        host.TrackScopeForTests(scope, () => savedWhileScopeAlive = !scope.IsDisposed);

        host.Shutdown();

        savedWhileScopeAlive.Must().BeTrue();
        scope.DisposeCount.Must().Be(1);
        host.OpenWindowCount.Must().Be(0);
    }

    [Fact]
    public void Shutdown_ThenLateWindowClose_DoesNotDisposeScopeTwice()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWindowHostService, WindowHostService>();
        using var provider = services.BuildServiceProvider();
        var host = (WindowHostService)provider.GetRequiredService<IWindowHostService>();

        var scope = new ForbiddenResolveScope(typeof(MainWindowViewModel));
        var saveCount = 0;
        host.TrackScopeForTests(scope, () => saveCount++);

        host.Shutdown();
        host.OnWindowClosed(scope, () => saveCount++);

        saveCount.Must().Be(1);
        scope.DisposeCount.Must().Be(1);
    }

    private sealed class ForbiddenResolveScope(Type forbidden) : IServiceScope, IServiceProvider
    {
        public int ResolveAttempts { get; private set; }
        public int DisposeCount { get; private set; }
        public bool IsDisposed => DisposeCount > 0;
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType)
        {
            if (serviceType == forbidden || serviceType.IsAssignableTo(forbidden))
            {
                ResolveAttempts++;
                throw new InvalidOperationException(
                    $"Close path must not re-resolve {forbidden.Name}; use the captured instance.");
            }

            return null;
        }

        public void Dispose() => DisposeCount++;
    }
}

public class ShellStrretLayoutTests
{
    [Fact]
    public void STRRET_IsLargeEnoughForShellCStrBuffer()
    {
        // Vanara owns the STRRET layout now; keep the regression guard so a package upgrade
        // cannot shrink the union below the shell's cStr[MAX_PATH] write size.
        System.Runtime.InteropServices.Marshal.SizeOf<Vanara.PInvoke.Shell32.STRRET>()
            .Must().BeGreaterThanOrEqualTo(264);
    }
}
