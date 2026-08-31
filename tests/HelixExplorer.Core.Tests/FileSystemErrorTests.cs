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

    [Fact]
    public void DescribeFileOperation_SameRoot_ReturnsCrossVolumeMessage()
    {
        var message = FileSystemError.DescribeFileOperation(
            new IOException("Source and destination path must have the same root."));
        message.Must().Be("Cannot move this folder across drives or network locations");
    }

    [Fact]
    public void DescribeFileOperation_InvalidOperation_KeepsMessage()
    {
        FileSystemError.DescribeFileOperation(new InvalidOperationException("Cannot copy a folder into itself."))
            .Must().Contain("itself");
    }
}
