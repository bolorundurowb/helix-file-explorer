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

    [Fact]
    public void DescribeFileOperation_same_root_is_cross_volume_message()
    {
        var message = FileSystemError.DescribeFileOperation(
            new IOException("Source and destination path must have the same root."));
        message.Must().Be("Cannot move this folder across drives or network locations");
    }

    [Fact]
    public void DescribeFileOperation_invalid_operation_keeps_message()
    {
        FileSystemError.DescribeFileOperation(new InvalidOperationException("Cannot copy a folder into itself."))
            .Must().Contain("itself");
    }
}
