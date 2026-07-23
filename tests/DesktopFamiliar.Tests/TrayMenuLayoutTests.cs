using DesktopFamiliar.Views.Controls;

namespace DesktopFamiliar.Tests;

public sealed class TrayMenuLayoutTests
{
    [Fact]
    public void RootRows_FillTheViewportMatchedMenuWidth()
    {
        using var menu = new ThemedTrayContextMenu();
        var toggle = new TrayToggleMenuItem("Focus", false, CreateToggleColors());
        var command = new ThemedTrayMenuItem("Configure");
        menu.Items.Add(toggle);
        menu.Items.Add(command);

        var preferred = menu.GetPreferredSize(System.Drawing.Size.Empty);
        menu.Size = preferred;
        menu.PerformLayout();

        Assert.Equal(ThemedTrayContextMenu.ContentWidth, preferred.Width);
        Assert.Equal(ThemedTrayContextMenu.ContentWidth - 36, toggle.Bounds.Width);
        Assert.Equal(ThemedTrayContextMenu.ContentWidth, command.Bounds.Width);
    }

    [Theory]
    [InlineData(true, true, 500, 500, true)]
    [InlineData(false, true, 110, 110, true)]
    [InlineData(false, true, 500, 500, false)]
    [InlineData(false, false, 110, 110, false)]
    public void SubmenuPointerPolicy_ClosesOnlyAfterPointerLeavesTheOwnerAndChild(
        bool ownerPointerOver,
        bool dropDownVisible,
        int pointerX,
        int pointerY,
        bool expected)
    {
        var dropDownBounds = new System.Drawing.Rectangle(100, 100, 100, 100);

        Assert.Equal(expected, TraySubmenuPointerPolicy.ShouldKeepOpen(
            ownerPointerOver,
            dropDownVisible,
            dropDownBounds,
            new System.Drawing.Point(pointerX, pointerY)));
    }

    private static TrayToggleColors CreateToggleColors() => new(
        System.Drawing.Color.White,
        System.Drawing.Color.Black,
        System.Drawing.Color.LightGray,
        System.Drawing.Color.Blue,
        System.Drawing.Color.DarkBlue,
        System.Drawing.Color.White,
        System.Drawing.Color.White,
        System.Drawing.Color.Blue);
}
