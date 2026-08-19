using Avalonia.Controls;
using HelixExplorer.Views;

namespace HelixExplorer.ViewModels.Tests;

public class PaneViewDoubleTapGuardTests
{
    [Fact]
    public void ShouldSuppressActivation_TextBox_IsTrue()
    {
        PaneView.ShouldSuppressActivation(new TextBox()).Must().BeTrue();
    }

    [Fact]
    public void ShouldSuppressActivation_TextBlockInARow_IsFalse()
    {
        PaneView.ShouldSuppressActivation(new TextBlock { Text = "readme.txt" }).Must().BeFalse();
    }

    [Fact]
    public void ShouldSuppressActivation_Null_IsFalse()
    {
        PaneView.ShouldSuppressActivation(null).Must().BeFalse();
    }
}
