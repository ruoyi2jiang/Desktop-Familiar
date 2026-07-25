namespace DesktopFamiliar.Models;

/// <summary>
/// Optional animations used while the user directly interacts with the
/// companion viewport. Keeping these assignments together makes room for
/// future interaction states without expanding the companion's root model.
/// </summary>
public sealed class CompanionInteractionStateAssignments
{
    public Guid? DragLeftAnimationId { get; set; }
    public Guid? DragRightAnimationId { get; set; }
    public Guid? DragHoldAnimationId { get; set; }
    public List<CompanionClickArea> ClickAreas { get; set; } = [];

    public Guid? GetAnimationId(CompanionInteractionState state) => state switch
    {
        CompanionInteractionState.DragLeft => DragLeftAnimationId,
        CompanionInteractionState.DragRight => DragRightAnimationId,
        CompanionInteractionState.DragHold => DragHoldAnimationId,
        _ => null
    };

    public void ClearAnimationId(Guid animationId)
    {
        if (DragLeftAnimationId == animationId)
            DragLeftAnimationId = null;
        if (DragRightAnimationId == animationId)
            DragRightAnimationId = null;
        if (DragHoldAnimationId == animationId)
            DragHoldAnimationId = null;
        foreach (var clickArea in ClickAreas ?? [])
        {
            if (clickArea.AnimationId == animationId)
                clickArea.AnimationId = null;
        }
    }

    public CompanionInteractionStateAssignments Clone() => new()
    {
        DragLeftAnimationId = DragLeftAnimationId,
        DragRightAnimationId = DragRightAnimationId,
        DragHoldAnimationId = DragHoldAnimationId,
        ClickAreas = ClickAreas?.Select(clickArea => clickArea.Clone()).ToList() ?? []
    };
}

public enum CompanionInteractionState
{
    DragLeft,
    DragRight,
    DragHold
}
