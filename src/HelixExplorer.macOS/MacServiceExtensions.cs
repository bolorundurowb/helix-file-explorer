using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.macOS.FileSystem;
using HelixExplorer.macOS.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace HelixExplorer.macOS;

public static class MacServiceExtensions
{
    public static IServiceCollection AddHelixMacServices(this IServiceCollection services)
    {
        services.AddSingleton<IShellFolderEnumerator, MacTrashEnumerator>();
        services.AddSingleton<IFileSystemProvider, MacFileSystemProvider>();
        services.AddSingleton<IQuickAccessProvider, MacQuickAccessProvider>();
        services.AddSingleton<IVolumeProvider, MacVolumeProvider>();
        services.AddSingleton<IVolumeChangeWatcher, MacVolumeChangeWatcher>();
        services.AddSingleton<INetworkDiscoveryAvailability, MacNetworkDiscoveryAvailability>();
        services.AddSingleton<INetworkLocationProvider, MacNetworkLocationProvider>();
        services.AddSingleton<INetworkConnectionService, MacNetworkConnectionService>();
        services.AddSingleton<IFileOperationService, MacFileOperationService>();
        services.AddSingleton<IShellContextMenuService, MacShellContextMenuService>();
        services.AddSingleton<ITerminalLauncher, MacTerminalLauncher>();
        services.AddSingleton<IExternalFileDragService, MacExternalFileDragService>();
        services.AddSingleton<IFileVisualProvider, MacFileVisualProvider>();
        return services;
    }
}