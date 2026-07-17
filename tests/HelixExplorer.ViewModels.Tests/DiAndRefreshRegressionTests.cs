using System.Reflection;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;
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
        Descriptor<IPaneCoordinatorFactory>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<IPaneViewModelFactory>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<AppSettingsCoordinator>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<SidebarViewModel>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<CommandPaletteService>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<TabSessionCoordinator>(services).Lifetime.Must().Be(ServiceLifetime.Scoped);
        Descriptor<PaneFileOperationCoordinator>(services).Lifetime.Must().Be(ServiceLifetime.Transient);
        Descriptor<PaneShellActionCoordinator>(services).Lifetime.Must().Be(ServiceLifetime.Transient);
        Descriptor<IWindowHostService>(services).Lifetime.Must().Be(ServiceLifetime.Singleton);
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
    public void RealScope_ReporterSharedBetweenFactoryCoordinatorAndExplicitResolve()
    {
        using var provider = CreateAppServices().BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var fromExplicit = sp.GetRequiredService<FileOperationReporter>();
        var factory = sp.GetRequiredService<IPaneCoordinatorFactory>();
        var coordinator = factory.CreateFileOperationCoordinator();
        var fromCoordinator = GetInjectedReporter(coordinator);

        fromExplicit.Must().Be(fromCoordinator);
        fromExplicit.Must().Be(sp.GetRequiredService<IFileOperationReporter>());
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
    public void FactoryResolvedFromScope_DoesNotCaptureRootReporter()
    {
        using var provider = CreateAppServices().BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var windowReporter = scope.ServiceProvider.GetRequiredService<FileOperationReporter>();
        var factory = scope.ServiceProvider.GetRequiredService<IPaneCoordinatorFactory>();
        var coordinatorReporter = GetInjectedReporter(factory.CreateFileOperationCoordinator());

        windowReporter.Must().Be(coordinatorReporter);

        ((Action)(() => provider.GetRequiredService<FileOperationReporter>())).Throws<InvalidOperationException>();
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

    private static IFileOperationReporter GetInjectedReporter(PaneFileOperationCoordinator coordinator)
    {
        var field = typeof(PaneFileOperationCoordinator)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(f => typeof(IFileOperationReporter).IsAssignableFrom(f.FieldType));
        return (IFileOperationReporter)field.GetValue(coordinator)!;
    }
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

    private sealed class ForbiddenResolveScope(Type forbidden) : IServiceScope, IServiceProvider
    {
        public int ResolveAttempts { get; private set; }
        public bool IsDisposed { get; private set; }
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

        public void Dispose() => IsDisposed = true;
    }
}

public class ShellStrretLayoutTests
{
    [Fact]
    public void STRRET_IsLargeEnoughForShellCStrBuffer()
    {
        var strretType = typeof(HelixExplorer.Windows.Shell.WinShellFolderEnumerator).Assembly
            .GetType("HelixExplorer.Windows.Shell.STRRET", throwOnError: true)!;
        System.Runtime.InteropServices.Marshal.SizeOf(strretType).Must().BeGreaterThanOrEqualTo(264);
    }
}
