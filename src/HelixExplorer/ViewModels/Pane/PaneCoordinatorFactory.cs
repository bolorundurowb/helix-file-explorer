using Microsoft.Extensions.DependencyInjection;

namespace HelixExplorer.ViewModels.Pane;

/// <summary>
/// Creates pane-scoped coordinators. Using a factory keeps <see cref="PaneViewModel"/> from
/// tracking the individual services required by each coordinator and ensures every pane gets
/// its own refresh and file-operation state.
/// </summary>
public interface IPaneCoordinatorFactory
{
    PaneRefreshCoordinator CreateRefreshCoordinator();

    PaneFileOperationCoordinator CreateFileOperationCoordinator();
}

public sealed class PaneCoordinatorFactory(IServiceProvider serviceProvider) : IPaneCoordinatorFactory
{
    public PaneRefreshCoordinator CreateRefreshCoordinator()
        => serviceProvider.GetRequiredService<PaneRefreshCoordinator>();

    public PaneFileOperationCoordinator CreateFileOperationCoordinator()
        => serviceProvider.GetRequiredService<PaneFileOperationCoordinator>();
}
