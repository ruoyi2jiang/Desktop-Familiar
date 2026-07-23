using DesktopFamiliar.Views.Controls;

namespace DesktopFamiliar.Tests;

public sealed class TrayMenuItemHighlightPolicyTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SubmenuOwner_IgnoresPersistentNativeState(bool isSelected, bool isPressed)
    {
        Assert.Equal(TrayMenuItemHighlightState.None, TrayMenuItemHighlightPolicy.Resolve(
            hasSubmenu: true,
            isPointerOver: false,
            isSubmenuOpen: false,
            isSelected,
            isPressed));
    }

    [Fact]
    public void SubmenuOwner_UsesHoverWhilePointerIsOverIt()
    {
        Assert.Equal(TrayMenuItemHighlightState.Hover, TrayMenuItemHighlightPolicy.Resolve(
            hasSubmenu: true,
            isPointerOver: true,
            isSubmenuOpen: false,
            isSelected: true,
            isPressed: true));
    }

    [Fact]
    public void SubmenuOwner_KeepsHoverWhileChildMenuIsOpen()
    {
        Assert.Equal(TrayMenuItemHighlightState.Hover, TrayMenuItemHighlightPolicy.Resolve(
            hasSubmenu: true,
            isPointerOver: false,
            isSubmenuOpen: true,
            isSelected: true,
            isPressed: true));
    }

    [Theory]
    [InlineData(true, false, TrayMenuItemHighlightState.Hover)]
    [InlineData(false, true, TrayMenuItemHighlightState.Pressed)]
    public void CommandRow_UsesNormalHoverAndPressedHighlight(
        bool isSelected,
        bool isPressed,
        TrayMenuItemHighlightState expected)
    {
        Assert.Equal(expected, TrayMenuItemHighlightPolicy.Resolve(
            hasSubmenu: false,
            isPointerOver: false,
            isSubmenuOpen: false,
            isSelected,
            isPressed));
    }

    [Fact]
    public void IdleCommandRow_IsNotHighlighted()
    {
        Assert.Equal(TrayMenuItemHighlightState.None, TrayMenuItemHighlightPolicy.Resolve(
            hasSubmenu: false,
            isPointerOver: false,
            isSubmenuOpen: false,
            isSelected: false,
            isPressed: false));
    }
}
