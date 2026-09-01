namespace HelixExplorer.ViewModels.Pane;

public interface IPaneOperationHost
{
    Task RefreshAfterOperationAsync();

    void SetOperationStatus(string text);
}
