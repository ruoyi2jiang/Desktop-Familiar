using System.Globalization;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

public sealed record IdleSceneSelection(
    AnimationSceneRole Role,
    IReadOnlyList<AnimationScene> Candidates);

/// <summary>
/// Selects the scene pool for an idle trigger. Each matching birthday pool owns
/// thirty percent of the probability range; the ordinary random-idle pool owns
/// the remainder.
/// </summary>
public static class IdleSceneSelectionPolicy
{
    public const double BirthdayProbability = 0.30;

    public static bool HasAnyCandidate(
        CompanionDefinition companion,
        DateTime? userBirthday,
        DateTime today) =>
        ScenePlaybackPolicy.GetRandomIdleCandidates(companion).Count > 0
        || GetSelfBirthdayCandidates(companion, today).Count > 0
        || GetUserBirthdayCandidates(companion, userBirthday, today).Count > 0;

    public static IdleSceneSelection? Select(
        CompanionDefinition companion,
        DateTime? userBirthday,
        DateTime today,
        double probabilitySample)
    {
        ArgumentNullException.ThrowIfNull(companion);
        if (probabilitySample is < 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(probabilitySample));

        var remaining = probabilitySample;
        var selfBirthday = GetSelfBirthdayCandidates(companion, today);
        if (selfBirthday.Count > 0)
        {
            if (remaining < BirthdayProbability)
                return new IdleSceneSelection(AnimationSceneRole.SelfBirthday, selfBirthday);
            remaining -= BirthdayProbability;
        }

        var userBirthdayScenes = GetUserBirthdayCandidates(companion, userBirthday, today);
        if (userBirthdayScenes.Count > 0)
        {
            if (remaining < BirthdayProbability)
                return new IdleSceneSelection(AnimationSceneRole.UserBirthday, userBirthdayScenes);
        }

        var randomIdle = ScenePlaybackPolicy.GetRandomIdleCandidates(companion);
        return randomIdle.Count == 0
            ? null
            : new IdleSceneSelection(AnimationSceneRole.IdleRandom, randomIdle);
    }

    private static IReadOnlyList<AnimationScene> GetSelfBirthdayCandidates(
        CompanionDefinition companion,
        DateTime today) =>
        MatchesBirthday(companion.Birthday, today)
            ? FindAssignedScenes(companion.ActiveScenes, companion.ActiveScenes.SelfBirthdayAnimation1Id, companion.ActiveScenes.SelfBirthdayAnimation2Id)
            : [];

    private static IReadOnlyList<AnimationScene> GetUserBirthdayCandidates(
        CompanionDefinition companion,
        DateTime? userBirthday,
        DateTime today) =>
        userBirthday is DateTime birthday && IsSameMonthAndDay(birthday, today)
            ? FindAssignedScenes(companion.ActiveScenes, companion.ActiveScenes.UserBirthdayAnimation1Id, companion.ActiveScenes.UserBirthdayAnimation2Id)
            : [];

    private static IReadOnlyList<AnimationScene> FindAssignedScenes(
        CompanionScenes companion,
        params Guid?[] ids) =>
        ids.Where(id => id is not null)
            .Distinct()
            .Select(companion.FindAnimation)
            .OfType<AnimationScene>()
            .ToList();

    private static bool MatchesBirthday(string value, DateTime today) =>
        (DateTime.TryParseExact(
             value,
             "yyyy-MM-dd",
             CultureInfo.InvariantCulture,
             DateTimeStyles.None,
             out var birthday)
         || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out birthday)
         || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out birthday))
        && IsSameMonthAndDay(birthday, today);

    private static bool IsSameMonthAndDay(DateTime first, DateTime second) =>
        first.Month == second.Month && first.Day == second.Day;
}
