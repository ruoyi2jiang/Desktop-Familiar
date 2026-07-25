using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Tests;

public sealed class CompanionInteractionStatePolicyTests
{
    [Theory]
    [InlineData(-1, CompanionInteractionState.DragLeft)]
    [InlineData(0, CompanionInteractionState.DragHold)]
    [InlineData(1, CompanionInteractionState.DragRight)]
    public void DragState_UsesHorizontalMovement(double horizontalMovement, CompanionInteractionState expected)
    {
        Assert.Equal(expected, CompanionInteractionStatePolicy.GetDragState(horizontalMovement));
    }

    [Fact]
    public void AssignedDragScene_OnlyUsesTheExactAssignedScene()
    {
        var idle = new AnimationScene { Name = "Idle" };
        var dragLeft = new AnimationScene { Name = "Drag left" };
        var companion = new CompanionDefinition
        {
            Animations = [idle, dragLeft],
            IdleDefaultAnimationId = idle.Id,
            InteractionStates = new CompanionInteractionStateAssignments
            {
                DragLeftAnimationId = dragLeft.Id
            }
        };

        Assert.Same(dragLeft, CompanionInteractionStatePolicy.GetAssignedDragScene(
            companion,
            CompanionInteractionState.DragLeft));
        Assert.Null(CompanionInteractionStatePolicy.GetAssignedDragScene(
            companion,
            CompanionInteractionState.DragHold));
    }

    [Fact]
    public void ClickScene_UsesTheFirstMatchingAssignedPercentageArea()
    {
        var idle = new AnimationScene { Name = "Idle" };
        var primary = new AnimationScene { Name = "Primary" };
        var later = new AnimationScene { Name = "Later" };
        var companion = new CompanionDefinition
        {
            Animations = [idle, primary, later],
            IdleDefaultAnimationId = idle.Id,
            InteractionStates = new CompanionInteractionStateAssignments
            {
                ClickAreas =
                [
                    new CompanionClickArea { LeftPercent = 0, TopPercent = 0, WidthPercent = 60, HeightPercent = 100, AnimationId = primary.Id },
                    new CompanionClickArea { LeftPercent = 0, TopPercent = 0, WidthPercent = 100, HeightPercent = 100, AnimationId = later.Id }
                ]
            }
        };

        Assert.True(CompanionInteractionStatePolicy.HasAnyClickScene(companion));
        Assert.Same(primary, CompanionInteractionStatePolicy.GetClickScene(companion, 30, 40, 100, 100));
        Assert.Same(later, CompanionInteractionStatePolicy.GetClickScene(companion, 90, 40, 100, 100));
        companion.InteractionStates.ClickAreas.Clear();
        Assert.False(CompanionInteractionStatePolicy.HasAnyClickScene(companion));
        Assert.Null(CompanionInteractionStatePolicy.GetClickScene(companion, 30, 40, 100, 100));
    }

    [Fact]
    public void ClickAreaWithoutAnimation_DoesNotRegisterAPlayableClickScene()
    {
        var idle = new AnimationScene { Name = "Idle" };
        var companion = new CompanionDefinition
        {
            Animations = [idle],
            IdleDefaultAnimationId = idle.Id,
            InteractionStates = new CompanionInteractionStateAssignments
            {
                ClickAreas = [new CompanionClickArea { WidthPercent = 100, HeightPercent = 100 }]
            }
        };

        Assert.False(CompanionInteractionStatePolicy.HasAnyClickScene(companion));
        Assert.Null(CompanionInteractionStatePolicy.GetClickScene(companion, 50, 50, 100, 100));
    }
}
