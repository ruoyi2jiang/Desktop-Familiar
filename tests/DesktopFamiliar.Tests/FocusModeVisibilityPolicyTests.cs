using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Focus;

namespace DesktopFamiliar.Tests;

public sealed class FocusModeVisibilityPolicyTests
{
    [Theory]
    [InlineData(false, false, CompanionActivityState.Idle, false)]
    [InlineData(true, false, CompanionActivityState.Idle, true)]
    [InlineData(true, false, CompanionActivityState.Reminder, true)]
    [InlineData(true, true, CompanionActivityState.Idle, false)]
    [InlineData(true, true, CompanionActivityState.Reminder, true)]
    public void ShouldDisplay_AppliesFocusMode(
        bool hasCurrentCompanion,
        bool focusModeEnabled,
        CompanionActivityState activityState,
        bool expected)
    {
        var actual = FocusModeVisibilityPolicy.ShouldDisplay(
            hasCurrentCompanion,
            focusModeEnabled,
            activityState);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CanOpenOverlayContextMenu_DisablesViewportMenuDuringFocus(
        bool focusModeEnabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            FocusModeVisibilityPolicy.CanOpenOverlayContextMenu(focusModeEnabled));
    }
}
