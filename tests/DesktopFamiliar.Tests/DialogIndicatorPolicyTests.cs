using DesktopFamiliar.Services.Dialogs;

namespace DesktopFamiliar.Tests;

public sealed class DialogIndicatorPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EllipsisIsHiddenUntilChatHasBeenHidden(bool hasActiveDialog)
    {
        var presentation = DialogIndicatorPolicy.Resolve(
            hasActiveDialog,
            hasDialogContent: true,
            dialogHidden: false,
            restoreIndicatorDismissed: false);

        Assert.False(presentation.ShowRestoreButton);
    }

    [Fact]
    public void HiddenActiveDialog_ShowsThemeToneEllipsis()
    {
        var presentation = DialogIndicatorPolicy.Resolve(
            hasActiveDialog: true,
            hasDialogContent: true,
            dialogHidden: true,
            restoreIndicatorDismissed: false);

        Assert.False(presentation.ShowBubble);
        Assert.True(presentation.ShowRestoreButton);
        Assert.Equal(DialogRestoreIndicatorTone.Active, presentation.RestoreTone);
    }

    [Fact]
    public void HiddenCompletedDialog_ShowsOverlayToneEllipsis()
    {
        var presentation = DialogIndicatorPolicy.Resolve(
            hasActiveDialog: false,
            hasDialogContent: false,
            dialogHidden: true,
            restoreIndicatorDismissed: false);

        Assert.True(presentation.ShowRestoreButton);
        Assert.Equal(DialogRestoreIndicatorTone.Inactive, presentation.RestoreTone);
    }

    [Fact]
    public void ClickingEllipsis_UnhidesBubbleAndDismissesIndicator()
    {
        var presentation = DialogIndicatorPolicy.Resolve(
            hasActiveDialog: true,
            hasDialogContent: true,
            dialogHidden: false,
            restoreIndicatorDismissed: true);

        Assert.True(presentation.ShowBubble);
        Assert.False(presentation.ShowRestoreButton);
    }

    [Fact]
    public void ActiveDialogWithoutContent_DoesNotShowEmptyBubble()
    {
        var presentation = DialogIndicatorPolicy.Resolve(
            hasActiveDialog: true,
            hasDialogContent: false,
            dialogHidden: false,
            restoreIndicatorDismissed: false);

        Assert.False(presentation.ShowBubble);
        Assert.False(presentation.ShowHideButton);
        Assert.Equal(DialogRestoreIndicatorTone.Inactive, presentation.RestoreTone);
    }
}
