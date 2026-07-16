using HelixExplorer.Core.Archives;
using HelixExplorer.Core.FileSystem;
using HelixExplorer.Core.Git;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Core.Settings;
using HelixExplorer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.ViewModels.Pane;

public interface IPaneViewModelFactory
{
    PaneViewModel Create();
}

public sealed class PaneViewModelFactory(IServiceProvider serviceProvider) : IPaneViewModelFactory
{
    public PaneViewModel Create()
    {
        var watcherFactory = serviceProvider.GetRequiredService<Func<IFileChangeWatcher>>();
        return new PaneViewModel(
            serviceProvider.GetRequiredService<IFileSystemProvider>(),
            serviceProvider.GetRequiredService<IArchiveProvider>(),
            serviceProvider.GetRequiredService<IFolderColorService>(),
            serviceProvider.GetRequiredService<IFileOperationService>(),
            serviceProvider.GetRequiredService<IClipboardService>(),
            serviceProvider.GetRequiredService<IUiHost>(),
            serviceProvider.GetRequiredService<IGitProvider>(),
            watcherFactory(),
            serviceProvider.GetRequiredService<ISettingsStore>(),
            serviceProvider.GetRequiredService<IQuickAccessProvider>(),
            serviceProvider.GetRequiredService<IUserDialogService>(),
            serviceProvider.GetRequiredService<IWindowHostService>(),
            serviceProvider.GetRequiredService<IShellFolderEnumerator>(),
            serviceProvider.GetRequiredService<IPaneCoordinatorFactory>(),
            serviceProvider.GetRequiredService<ILogger<PaneViewModel>>());
    }
}
