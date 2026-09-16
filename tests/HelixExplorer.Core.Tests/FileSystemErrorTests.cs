using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public sealed class FileSystemErrorTests
{
    [Fact]
    public void Describe_UnauthorizedAccessException_ReturnsAccessDenied()
    {
        FileSystemErrorMessages.Describe(new UnauthorizedAccessException()).Must().Be("Access denied");
    }

    [Fact]
    public void Describe_NetworkPathIOException_ReturnsUnavailable()
    {
        var message = FileSystemErrorMessages.Describe(
            new IOException("The network path was not found."),
            @"\\server\share");
        message.Must().Be("Network location is unavailable");
    }

    [Fact]
    public void DescribeFileOperation_SameRoot_ReturnsCrossVolumeMessage()
    {
        var message = FileSystemErrorMessages.DescribeFileOperation(
            new IOException("Source and destination path must have the same root."));
        message.Must().Be("Cannot move this folder across drives or network locations");
    }

    [Fact]
    public void DescribeFileOperation_InvalidOperation_KeepsMessage()
    {
        FileSystemErrorMessages.DescribeFileOperation(new InvalidOperationException("Cannot copy a folder into itself."))
            .Must().Contain("itself");
    }

    [Fact]
    public void Describe_DirectoryNotFound_ReturnsFolderNotFound()
    {
        FileSystemErrorMessages.Describe(new DirectoryNotFoundException()).Must().Be("Folder not found");
    }

    [Fact]
    public void Describe_OperationCanceled_ReturnsEmpty()
    {
        FileSystemErrorMessages.Describe(new OperationCanceledException()).Must().Be(string.Empty);
    }
}

public sealed class FileSystemErrorClassifierTests
{
    [Fact]
    public void FromException_MapsKnownKinds()
    {
        FileSystemErrorClassifier.FromException(new UnauthorizedAccessException()).Must().Be(FileSystemError.AccessDenied);
        FileSystemErrorClassifier.FromException(new DirectoryNotFoundException()).Must().Be(FileSystemError.PathNotFound);
        FileSystemErrorClassifier.FromException(new FileNotFoundException()).Must().Be(FileSystemError.PathNotFound);
        FileSystemErrorClassifier.FromException(new PathTooLongException()).Must().Be(FileSystemError.PathTooLong);
        FileSystemErrorClassifier.FromException(new NotSupportedException()).Must().Be(FileSystemError.NotSupported);
    }

    [Fact]
    public void FromException_MapsIoFileInUseByHResult()
    {
        var ex = new IOException("The process cannot access the file because it is being used by another process.")
        {
            HResult = unchecked((int)0x80070020),
        };

        FileSystemErrorClassifier.FromException(ex).Must().Be(FileSystemError.FileInUse);
    }

    [Fact]
    public void FromException_MapsIoDiskFullByHResult()
    {
        var ex = new IOException("There is not enough space on the disk.")
        {
            HResult = unchecked((int)0x80070070),
        };

        FileSystemErrorClassifier.FromException(ex).Must().Be(FileSystemError.DiskFull);
    }

    [Fact]
    public void FromException_MapsUnknownIoToUnknown()
    {
        FileSystemErrorClassifier.FromException(new IOException("Some other IO failure"))
            .Must().Be(FileSystemError.Unknown);
    }

    [Fact]
    public void FromException_RejectsNull()
    {
        Xunit.Assert.Throws<ArgumentNullException>(() => FileSystemErrorClassifier.FromException(null!));
    }
}
