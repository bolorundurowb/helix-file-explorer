using HelixExplorer.Core.FileSystem;

namespace HelixExplorer.Core.Tests;

public sealed class FileListPressPolicyTests
{
    [Fact]
    public void Preserve_WhenGroupMemberPressedWithoutModifiers()
    {
        FileListPressPolicy.ShouldPreserveGroupOnPress(3, pressedIsSelected: true, modifiersDown: false)
            .Must().BeTrue();
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(3, false, false)]
    [InlineData(3, true, true)]
    [InlineData(0, true, false)]
    public void DoNotPreserve_WhenNotAPlainGroupPress(
        int selectedCount,
        bool pressedIsSelected,
        bool modifiersDown)
    {
        FileListPressPolicy.ShouldPreserveGroupOnPress(selectedCount, pressedIsSelected, modifiersDown)
            .Must().BeFalse();
    }
}
