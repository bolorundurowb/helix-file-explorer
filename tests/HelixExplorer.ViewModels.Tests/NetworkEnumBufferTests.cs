using HelixExplorer.Windows.FileSystem;
using Xunit;

namespace HelixExplorer.ViewModels.Tests;

public class NetworkEnumBufferTests
{
    [Fact]
    public void Grow_DoublesFromInitial()
    {
        Assert.Equal(NetworkEnumBuffer.InitialSize * 2, NetworkEnumBuffer.Grow(NetworkEnumBuffer.InitialSize));
    }

    [Fact]
    public void Grow_ZeroOrNegative_ReturnsInitial()
    {
        Assert.Equal(NetworkEnumBuffer.InitialSize, NetworkEnumBuffer.Grow(0));
        Assert.Equal(NetworkEnumBuffer.InitialSize, NetworkEnumBuffer.Grow(-100));
    }

    [Fact]
    public void Grow_IsCappedAtMax()
    {
        Assert.Equal(NetworkEnumBuffer.MaxSize, NetworkEnumBuffer.Grow(NetworkEnumBuffer.MaxSize));
        Assert.Equal(NetworkEnumBuffer.MaxSize, NetworkEnumBuffer.Grow(NetworkEnumBuffer.MaxSize - 1));
    }

    [Fact]
    public void Grow_HonorsRequestedSize_WhenLargerThanDouble()
    {
        Assert.Equal(128 * 1024, NetworkEnumBuffer.Grow(16 * 1024, 128 * 1024));
    }

    [Fact]
    public void Grow_NeverExceedsMax_AcrossRepeatedGrowth()
    {
        var size = NetworkEnumBuffer.InitialSize;
        for (var i = 0; i < 20; i++)
            size = NetworkEnumBuffer.Grow(size);

        Assert.Equal(NetworkEnumBuffer.MaxSize, size);
    }
}
