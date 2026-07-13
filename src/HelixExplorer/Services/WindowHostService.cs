using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HelixExplorer.Core.Session;
using HelixExplorer.ViewModels;
using HelixExplorer.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HelixExplorer.Services;

public interface IWindowHostService
{
    Task<MainWindow> OpenWindowAsync(string? initialPath = null, bool restoreSession = false);

    int OpenWindowCount { get; }
}

public sealed class WindowHostService(IServiceScopeFactory scopeFactory) : IWindowHostService
{
    private readonly object _gate = new();
    private readonly List<IServiceScope> _scopes = new();

    public int OpenWindowCount
    {
        get
        {
            lock (_gate)
                return _scopes.Count;
        }
    }

    public async Task<MainWindow> OpenWindowAsync(string? initialPath = null, bool restoreSession = false)
    {
        var scope = scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<MainWindow>();
        var vm = scope.ServiceProvider.GetRequiredService<MainWindowViewModel>();
        scope.ServiceProvider.GetRequiredService<IWindowOwnerContext>().SetOwner(window);
        window.DataContext = vm;

        vm.InitializeWindow(restoreSession, initialPath);

        window.Closed += (_, _) => OnWindowClosed(scope);

        lock (_gate)
            _scopes.Add(scope);

        window.Show();
        await Task.CompletedTask.ConfigureAwait(true);
        return window;
    }

    private void OnWindowClosed(IServiceScope scope)
    {
        MainWindowViewModel? vm = null;
        if (scope.ServiceProvider.GetService<MainWindowViewModel>() is { } viewModel)
            vm = viewModel;

        lock (_gate)
        {
            _scopes.Remove(scope);
            if (_scopes.Count == 0 && vm is not null)
                vm.SaveSession();
        }

        vm?.Dispose();
        scope.Dispose();
    }
}
