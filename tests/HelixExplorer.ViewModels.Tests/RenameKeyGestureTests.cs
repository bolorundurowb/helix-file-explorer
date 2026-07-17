using Avalonia.Input;
using HelixExplorer.Input;

namespace HelixExplorer.ViewModels.Tests;

public class RenameKeyGestureTests
{
    [Fact]
    public void Enter_Commits()
    {
        RenameKeyGesture.Resolve(Key.Enter, KeyModifiers.None).Must().Be(RenameKeyAction.Commit);
    }

    [Fact]
    public void Escape_Cancels()
    {
        RenameKeyGesture.Resolve(Key.Escape, KeyModifiers.None).Must().Be(RenameKeyAction.Cancel);
    }

    [Theory]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    [InlineData(Key.Home)]
    [InlineData(Key.End)]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.PageUp)]
    [InlineData(Key.PageDown)]
    public void NavigationKeys_AreContainedInEditor(Key key)
    {
        RenameKeyGesture.Resolve(key, KeyModifiers.None).Must().Be(RenameKeyAction.Contain);
    }

    [Theory]
    [InlineData(KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift)]
    public void ModifiedNavigationKeys_StayContained(KeyModifiers modifiers)
    {
        RenameKeyGesture.Resolve(Key.Left, modifiers).Must().Be(RenameKeyAction.Contain);
        RenameKeyGesture.Resolve(Key.Home, modifiers).Must().Be(RenameKeyAction.Contain);
    }

    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.Z)]
    [InlineData(Key.D1)]
    [InlineData(Key.Space)]
    [InlineData(Key.Back)]
    [InlineData(Key.Delete)]
    public void TypingKeys_BubbleNormally(Key key)
    {
        RenameKeyGesture.Resolve(key, KeyModifiers.None).Must().Be(RenameKeyAction.None);
    }
}
