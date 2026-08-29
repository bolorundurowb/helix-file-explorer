using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public sealed class FileSystemErrorTests
{
    [Fact]
    public void Describe_UnauthorizedAccessException_ReturnsAccessDenied()
    {
        FileSystemError.Describe(new UnauthorizedAccessException()).Must().Be("Access denied");
    }

    [Fact]
    public void Describe_NetworkPathIOException_ReturnsUnavailable()
    {
        var message = FileSystemError.Describe(
            new IOException("The network path was not found."),
            @"\\server\share");
        message.Must().Be("Network location is unavailable");
    }
}
