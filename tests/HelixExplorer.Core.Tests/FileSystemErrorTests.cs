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

    // CORE-9: classification used to rely entirely on ex.Message substrings, which are only in
    // English on an English-language Windows install. A non-English message that still carries
    // the real Win32 error as its HResult must classify the same way an English one would.
    [Fact]
    public void Describe_AccessDeniedHResult_ReturnsAccessDeniedRegardlessOfMessageLanguage()
    {
        var ex = new IOException("Accès refusé.") { HResult = unchecked((int)0x80070005) };

        FileSystemError.Describe(ex).Must().Be("Access denied");
    }

    [Fact]
    public void Describe_DiskFullHResult_ReturnsNotEnoughDiskSpace()
    {
        var ex = new IOException("Disque plein.") { HResult = unchecked((int)0x80070070) };

        FileSystemError.Describe(ex).Must().Be("Not enough disk space");
    }

    [Fact]
    public void DescribeFileOperation_SharingViolationHResult_ReturnsFileInUse()
    {
        var ex = new IOException("Le processus ne peut pas accéder au fichier.")
        {
            HResult = unchecked((int)0x80070020)
        };

        FileSystemError.DescribeFileOperation(ex).Must().Be("The file is in use by another program");
    }

    [Fact]
    public void DescribeFileOperation_SameRootHResult_ReturnsCrossVolumeMessage()
    {
        var ex = new IOException("Déplacement impossible.") { HResult = unchecked((int)0x80070011) };

        FileSystemError.DescribeFileOperation(ex).Must().Be("Cannot move this folder across drives or network locations");
    }
}
