using HelixExplorer.Core.Search;

namespace HelixExplorer.Core.Tests;

public sealed class TextFileClassifierTests
{
    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".TXT", true)]
    [InlineData(".cs", true)]
    [InlineData(".md", true)]
    [InlineData(".gitignore", true)]
    [InlineData(".dockerfile", true)]
    [InlineData(".exe", false)]
    [InlineData(".zip", false)]
    [InlineData(".png", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLikelyTextExtension_ChecksKnownExtensions(string? extension, bool expected)
    {
        TextFileClassifier.IsLikelyTextExtension(extension).Must().Be(expected);
    }

    [Fact]
    public void LooksBinary_EmptySample_ReturnsFalse()
    {
        TextFileClassifier.LooksBinary(ReadOnlySpan<byte>.Empty).Must().BeFalse();
    }

    [Fact]
    public void LooksBinary_SampleWithoutNulByte_ReturnsFalse()
    {
        var sample = "hello world"u8.ToArray();
        TextFileClassifier.LooksBinary(sample).Must().BeFalse();
    }

    [Fact]
    public void LooksBinary_SampleContainingNulByte_ReturnsTrue()
    {
        var sample = new byte[] { (byte)'a', (byte)'b', 0, (byte)'c' };
        TextFileClassifier.LooksBinary(sample).Must().BeTrue();
    }

    [Fact]
    public void LooksBinary_NulByteWithinFirst8192Bytes_IsDetected()
    {
        var sample = new byte[8300];
        Array.Fill(sample, (byte)'x');
        sample[8191] = 0;

        TextFileClassifier.LooksBinary(sample).Must().BeTrue();
    }

    [Fact]
    public void LooksBinary_NulByteAfterFirst8192Bytes_IsIgnored()
    {
        var sample = new byte[8300];
        Array.Fill(sample, (byte)'x');
        sample[8192] = 0;

        TextFileClassifier.LooksBinary(sample).Must().BeFalse();
    }

    [Fact]
    public void DefaultMaxBytes_IsOneMebibyte()
    {
        TextFileClassifier.DefaultMaxBytes.Must().Be(1 * 1024 * 1024);
    }
}
