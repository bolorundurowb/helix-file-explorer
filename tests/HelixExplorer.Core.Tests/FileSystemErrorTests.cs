using HelixExplorer.Core.FileSystem;
using Xunit;

namespace HelixExplorer.Core.Tests;

public sealed class FileSystemErrorTests
{
    [Fact]
    public void Describe_unauthorized_is_access_denied()
        => Assert.Equal("Access denied", FileSystemError.Describe(new UnauthorizedAccessException()));

    [Fact]
    public void Describe_network_path_io_is_unavailable()
    {
        var message = FileSystemError.Describe(
            new IOException("The network path was not found."),
            @"\\server\share");
        Assert.Equal("Network location is unavailable", message);
    }
}
