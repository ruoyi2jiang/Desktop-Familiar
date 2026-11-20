using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

/// <summary>
/// Resolves direct viewport interactions to their optional scene assignments.
/// This policy has no WPF dependency so the overlay can avoid subscribing an
/// interaction to playback unless that exact interaction has a scene.
/// </summary>
public static class CompanionInteractionStatePolicy
{
    public static CompanionInteractionState GetDragState(double horizontalMovement) =>
        horizontalMovement < 0
            ? CompanionInteractionState.DragLeft
            : horizontalMovement > 0
                ? CompanionInteractionState.DragRight
                : CompanionInteractionState.DragHold;

    public static AnimationScene? GetAssignedDragScene(
        CompanionDefinition companion,
        CompanionInteractionState state)
    {
        ArgumentNullException.ThrowIfNull(companion);
        return companion.ActiveScenes.GetInteractionAnimation(state);
    }

    public static bool HasAnyClickScene(CompanionDefinition companion)
    {
        ArgumentNullException.ThrowIfNull(companion);
        var scenes = companion.ActiveScenes;
        return scenes.InteractionStates?.ClickAreas?.Any(area => scenes.FindAnimation(area.AnimationId) is not null) == true;
    }

    public static AnimationScene? GetClickScene(
        CompanionDefinition companion,
        double horizontalPosition,
        double verticalPosition,
        double viewportWidth,
        double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(companion);
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return null;

        var horizontalPercent = horizontalPosition / viewportWidth * 100d;
        var verticalPercent = verticalPosition / viewportHeight * 100d;
        var scenes = companion.ActiveScenes;
        var clickArea = scenes.InteractionStates?.ClickAreas?.FirstOrDefault(area =>
            horizontalPercent >= area.LeftPercent
            && horizontalPercent <= area.LeftPercent + area.WidthPercent
            && verticalPercent >= area.TopPercent
            && verticalPercent <= area.TopPercent + area.HeightPercent);
        return scenes.FindAnimation(clickArea?.AnimationId);
    }

    public static bool IsDragState(CompanionInteractionState state) => state is
        CompanionInteractionState.DragLeft or
        CompanionInteractionState.DragRight or
        CompanionInteractionState.DragHold;
}
