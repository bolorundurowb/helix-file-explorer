using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public sealed class FileSystemErrorTests
{
    [Fact]
    public void Describe_unauthorized_is_access_denied()
    {
        FileSystemError.Describe(new UnauthorizedAccessException()).Must().Be("Access denied");
    }

    [Fact]
    public void Describe_network_path_io_is_unavailable()
    {
        var message = FileSystemError.Describe(
            new IOException("The network path was not found."),
            @"\\server\share");
        message.Must().Be("Network location is unavailable");
    }
}
