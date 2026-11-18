using System.Runtime.InteropServices;
using DesktopFamiliar.Views.Controls;
using Forms = System.Windows.Forms;

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
    [InlineData(false)]
    [InlineData(true)]
    public void FocusToggleClosesNativeMenuBeforeChangingWindowFocus(bool initiallyEnabled)
    {
        using var menu = new ThemedTrayContextMenu();
        var toggle = new TrayToggleMenuItem("Focus", initiallyEnabled, CreateToggleColors());
        menu.Items.Add(toggle);
        Forms.ToolStripDropDownCloseReason? closed = null;
        menu.Closed += (_, e) => closed = e.CloseReason;
        var changes = new List<(bool Enabled, bool MenuVisible, Forms.ToolStripDropDownCloseReason? Reason)>();
        toggle.Toggled += (_, _) => changes.Add((toggle.IsChecked, menu.Visible, closed));

        menu.Show(System.Drawing.Point.Empty);
        // Deliver Space to the hosted switch without sending input to any other window.
        SendMessage(toggle.Control.Handle, 0x0100, (nint)Forms.Keys.Space, 1);

        var change = Assert.Single(changes);
        Assert.Equal(!initiallyEnabled, change.Enabled);
        Assert.False(change.MenuVisible);
        Assert.Equal(Forms.ToolStripDropDownCloseReason.CloseCalled, change.Reason);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

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
