using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Focus;

/// <summary>
/// Determines whether the overlay should be presented. Normal mode always
/// presents the selected companion; focus mode suppresses idle and presents only
/// attention-requiring runtime states such as a calendar reminder.
/// </summary>
public static class FocusModeVisibilityPolicy
{
    public static bool ShouldDisplay(
        bool hasCurrentCompanion,
        bool focusModeEnabled,
        CompanionActivityState activityState) =>
        hasCurrentCompanion && (!focusModeEnabled || activityState != CompanionActivityState.Idle);

    public static bool CanOpenOverlayContextMenu(bool focusModeEnabled) =>
        !focusModeEnabled;
}
