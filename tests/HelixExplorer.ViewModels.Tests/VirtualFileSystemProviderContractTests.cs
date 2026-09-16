namespace HelixExplorer.ViewModels.Tests;

public sealed class VirtualFileSystemProviderContractTests : IFileSystemProviderContractTests
{
    public VirtualFileSystemProviderContractTests()
        : base(new VirtualFileSystemHarness())
    {
    }
}
