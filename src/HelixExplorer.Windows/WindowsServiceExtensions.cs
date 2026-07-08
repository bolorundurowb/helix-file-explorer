using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace HelixExplorer.Windows;

public static class WindowsServiceExtensions
{
    public static IServiceCollection AddHelixWindowsServices(this IServiceCollection services)
    {
        services.AddSingleton<IFileSystemProvider, FileSystem.WinFileSystemProvider>();
        services.AddSingleton<IQuickAccessProvider, FileSystem.WinQuickAccessProvider>();
        services.AddSingleton<IVolumeProvider, FileSystem.WinVolumeProvider>();
        services.AddSingleton<IFileOperationService, FileSystem.WinFileOperationService>();
        services.AddTransient<IFileChangeWatcher, FileSystem.FileChangeWatcherService>();
        services.AddSingleton<Func<IFileChangeWatcher>>(sp => () => sp.GetRequiredService<IFileChangeWatcher>());
        return services;
    }
}
