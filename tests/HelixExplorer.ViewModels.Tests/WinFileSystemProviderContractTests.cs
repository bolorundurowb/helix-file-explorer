namespace HelixExplorer.ViewModels.Tests;

public sealed class WinFileSystemProviderContractTests : IFileSystemProviderContractTests
{
    public WinFileSystemProviderContractTests()
        : base(new RealFileSystemHarness())
    {
    }
}
