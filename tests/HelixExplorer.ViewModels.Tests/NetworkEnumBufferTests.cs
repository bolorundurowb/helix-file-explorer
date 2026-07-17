using HelixExplorer.Windows.FileSystem;

namespace HelixExplorer.ViewModels.Tests;

public class NetworkEnumBufferTests
{
    [Fact]
    public void Grow_DoublesFromInitial()
    {
        NetworkEnumBuffer.Grow(NetworkEnumBuffer.InitialSize).Must().Be(NetworkEnumBuffer.InitialSize * 2);
    }

    [Fact]
    public void Grow_ZeroOrNegative_ReturnsInitial()
    {
        NetworkEnumBuffer.Grow(0).Must().Be(NetworkEnumBuffer.InitialSize);
        NetworkEnumBuffer.Grow(-100).Must().Be(NetworkEnumBuffer.InitialSize);
    }

    [Fact]
    public void Grow_IsCappedAtMax()
    {
        NetworkEnumBuffer.Grow(NetworkEnumBuffer.MaxSize).Must().Be(NetworkEnumBuffer.MaxSize);
        NetworkEnumBuffer.Grow(NetworkEnumBuffer.MaxSize - 1).Must().Be(NetworkEnumBuffer.MaxSize);
    }

    [Fact]
    public void Grow_HonorsRequestedSize_WhenLargerThanDouble()
    {
        NetworkEnumBuffer.Grow(16 * 1024, 128 * 1024).Must().Be(128 * 1024);
    }

    [Fact]
    public void Grow_NeverExceedsMax_AcrossRepeatedGrowth()
    {
        var size = NetworkEnumBuffer.InitialSize;
        for (var i = 0; i < 20; i++)
            size = NetworkEnumBuffer.Grow(size);

        size.Must().Be(NetworkEnumBuffer.MaxSize);
    }
}
