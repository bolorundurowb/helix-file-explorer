using Avalonia.Input;
using HelixExplorer.Input;
using Xunit;

namespace HelixExplorer.ViewModels.Tests;

public class RenameKeyGestureTests
{
    [Fact]
    public void Enter_Commits()
    {
        Assert.Equal(RenameKeyAction.Commit, RenameKeyGesture.Resolve(Key.Enter, KeyModifiers.None));
    }

    [Fact]
    public void Escape_Cancels()
    {
        Assert.Equal(RenameKeyAction.Cancel, RenameKeyGesture.Resolve(Key.Escape, KeyModifiers.None));
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
        Assert.Equal(RenameKeyAction.Contain, RenameKeyGesture.Resolve(key, KeyModifiers.None));
    }

    [Theory]
    [InlineData(KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift)]
    public void ModifiedNavigationKeys_StayContained(KeyModifiers modifiers)
    {
        // Shift+Left extends a selection, Ctrl+Left jumps a word: both are text-editing gestures.
        Assert.Equal(RenameKeyAction.Contain, RenameKeyGesture.Resolve(Key.Left, modifiers));
        Assert.Equal(RenameKeyAction.Contain, RenameKeyGesture.Resolve(Key.Home, modifiers));
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
        // Character/edit keys are handled by the TextBox itself and need no special containment.
        Assert.Equal(RenameKeyAction.None, RenameKeyGesture.Resolve(key, KeyModifiers.None));
    }
}
