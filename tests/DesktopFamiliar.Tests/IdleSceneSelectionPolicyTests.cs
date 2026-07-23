using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Tests;

public sealed class IdleSceneSelectionPolicyTests
{
    private static readonly DateTime Today = new(2026, 7, 21);

    [Fact]
    public void CompanionBirthdayPoolOwnsThirtyPercentAndThenFallsBackToRandomIdle()
    {
        var companion = CreateCompanion();
        companion.Birthday = "2000-07-21";

        var birthday = IdleSceneSelectionPolicy.Select(companion, null, Today, 0.29);
        var random = IdleSceneSelectionPolicy.Select(companion, null, Today, 0.30);

        Assert.Equal(AnimationSceneRole.SelfBirthday, birthday?.Role);
        Assert.Equal(2, birthday?.Candidates.Count);
        Assert.Equal(AnimationSceneRole.IdleRandom, random?.Role);
    }

    [Fact]
    public void MatchingCompanionAndUserBirthdaysEachOwnThirtyPercent()
    {
        var companion = CreateCompanion();
        companion.Birthday = "2000-07-21";

        var self = IdleSceneSelectionPolicy.Select(companion, Today.AddYears(-20), Today, 0.10);
        var user = IdleSceneSelectionPolicy.Select(companion, Today.AddYears(-20), Today, 0.45);
        var random = IdleSceneSelectionPolicy.Select(companion, Today.AddYears(-20), Today, 0.75);

        Assert.Equal(AnimationSceneRole.SelfBirthday, self?.Role);
        Assert.Equal(AnimationSceneRole.UserBirthday, user?.Role);
        Assert.Equal(AnimationSceneRole.IdleRandom, random?.Role);
    }

    [Fact]
    public void BirthdaySceneCanArmTimerWithoutOrdinaryRandomIdleScene()
    {
        var companion = CreateCompanion();
        companion.Birthday = "2000-07-21";
        companion.IdleRandomAnimationIds.Clear();

        Assert.True(IdleSceneSelectionPolicy.HasAnyCandidate(companion, null, Today));
        Assert.Null(IdleSceneSelectionPolicy.Select(companion, null, Today, 0.75));
    }

    [Fact]
    public void BirthdayAssignmentsAreIgnoredOnOtherDates()
    {
        var companion = CreateCompanion();
        companion.Birthday = "2000-07-20";

        var selection = IdleSceneSelectionPolicy.Select(companion, null, Today, 0.10);

        Assert.Equal(AnimationSceneRole.IdleRandom, selection?.Role);
    }

    private static CompanionDefinition CreateCompanion()
    {
        var idle = new AnimationScene { Name = "Idle" };
        var random = new AnimationScene { Name = "Random" };
        var selfOne = new AnimationScene { Name = "Self one" };
        var selfTwo = new AnimationScene { Name = "Self two" };
        var userOne = new AnimationScene { Name = "User one" };
        return new CompanionDefinition
        {
            Animations = [idle, random, selfOne, selfTwo, userOne],
            IdleDefaultAnimationId = idle.Id,
            IdleRandomAnimationIds = [random.Id],
            SelfBirthdayAnimation1Id = selfOne.Id,
            SelfBirthdayAnimation2Id = selfTwo.Id,
            UserBirthdayAnimation1Id = userOne.Id
        };
    }
}
