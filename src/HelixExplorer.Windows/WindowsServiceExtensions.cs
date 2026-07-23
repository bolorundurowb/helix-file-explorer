using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Windows.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace HelixExplorer.Windows;

public static class WindowsServiceExtensions
{
    public static IServiceCollection AddHelixWindowsServices(this IServiceCollection services)
    {
        services.AddSingleton<IShellFolderEnumerator, Shell.WinShellFolderEnumerator>();
        services.AddSingleton<IFileSystemProvider, FileSystem.WinFileSystemProvider>();
        services.AddSingleton<IQuickAccessProvider, FileSystem.WinQuickAccessProvider>();
        services.AddSingleton<IVolumeProvider, FileSystem.WinVolumeProvider>();
        services.AddSingleton<IVolumeChangeWatcher, FileSystem.WinVolumeChangeWatcher>();
        services.AddSingleton<INetworkDiscoveryAvailability, FileSystem.WinNetworkDiscoveryAvailability>();
        services.AddSingleton<INetworkLocationProvider, FileSystem.WinNetworkLocationProvider>();
        services.AddSingleton<INetworkConnectionService, FileSystem.WinNetworkConnectionService>();
        services.AddSingleton<IFileOperationService, FileSystem.WinFileOperationService>();
        services.AddSingleton<IShellContextMenuService, Shell.WinShellContextMenuService>();
        services.AddSingleton<ITerminalLauncher, WinTerminalLauncher>();
        services.AddSingleton<IExternalFileDragService, WinFormsExternalFileDragService>();
        services.AddSingleton<IFileVisualProvider, Shell.WinFileVisualProvider>();
        services.AddTransient<IFileChangeWatcher, FileSystem.FileChangeWatcherService>();
        services.AddScoped<Func<IFileChangeWatcher>>(sp => () => sp.GetRequiredService<IFileChangeWatcher>());
        return services;
    }
}
