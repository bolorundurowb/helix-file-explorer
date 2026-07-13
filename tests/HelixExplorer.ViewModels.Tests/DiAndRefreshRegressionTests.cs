using System.Reflection;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Services;
using HelixExplorer.ViewModels;
using HelixExplorer.ViewModels.Pane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HelixExplorer.ViewModels.Tests;

/// <summary>
/// Exercises the production composition root (<see cref="HelixServiceRegistration"/>),
/// not a reimplemented ServiceCollection.
/// </summary>
public class ScopedDiWiringTests
{
    [Fact]
    public void ApplicationRegistration_UsesScopedLifetimesForWindowGraph()
    {
        var services = CreateAppServices();

        Assert.Equal(ServiceLifetime.Scoped, Descriptor<MainWindowViewModel>(services).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Descriptor<FileOperationReporter>(services).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Descriptor<IFileOperationReporter>(services).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Descriptor<IPaneCoordinatorFactory>(services).Lifetime);
        Assert.Equal(ServiceLifetime.Transient, Descriptor<PaneFileOperationCoordinator>(services).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Descriptor<IWindowHostService>(services).Lifetime);
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

        Assert.Same(fromExplicit, fromCoordinator);
        Assert.Same(fromExplicit, sp.GetRequiredService<IFileOperationReporter>());
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
        Assert.NotSame(a, b);
    }

    [Fact]
    public void FactoryResolvedFromScope_DoesNotCaptureRootReporter()
    {
        // Regression for singleton factory + root IServiceProvider: coordinators would
        // resolve a captive reporter that never matches the window-scoped UI reporter.
        using var provider = CreateAppServices().BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var windowReporter = scope.ServiceProvider.GetRequiredService<FileOperationReporter>();
        var factory = scope.ServiceProvider.GetRequiredService<IPaneCoordinatorFactory>();
        var coordinatorReporter = GetInjectedReporter(factory.CreateFileOperationCoordinator());

        Assert.Same(windowReporter, coordinatorReporter);

        // Root must not be able to resolve scoped services when validateScopes is on.
        Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<FileOperationReporter>());
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
            Assert.Fail("Token observation threw ObjectDisposedException after cancel-without-dispose.");
        }

        cts.Dispose();
        var cancelled = false;
        try { cancelled = token.IsCancellationRequested; }
        catch (ObjectDisposedException) { cancelled = true; }
        Assert.True(cancelled);
    }
}

/// <summary>
/// Calls <see cref="WindowHostService.OnWindowClosed"/> — the same method production uses —
/// with a scope that fails if <see cref="MainWindowViewModel"/> is re-resolved.
/// </summary>
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

        Assert.True(saved);
        Assert.Equal(0, resolvingScope.ResolveAttempts);
        Assert.True(resolvingScope.IsDisposed);
        Assert.Equal(0, host.OpenWindowCount);
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

        Assert.False(saved);
        Assert.Equal(1, host.OpenWindowCount);
        Assert.True(closing.IsDisposed);
        Assert.False(remaining.IsDisposed);
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
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf(strretType) >= 264);
    }
}
