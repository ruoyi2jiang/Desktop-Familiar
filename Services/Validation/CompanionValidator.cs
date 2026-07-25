using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Validation;

/// <summary>Validates a companion definition, including its referenced local assets.</summary>
public static class CompanionValidator
{
    public static IReadOnlyList<string> Validate(CompanionDefinition companion)
    {
        ArgumentNullException.ThrowIfNull(companion);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(companion.Name))
            errors.Add(LocalizationService.Get("Validation.CompanionName"));
        if (companion.Animations.Count == 0)
            errors.Add(LocalizationService.Get("Validation.AnimationRequired"));
        if (companion.Animations.Any(scene => !HasValidAnimationSource(scene)))
            errors.Add(LocalizationService.Get("Validation.AnimationSource"));
        if (companion.Animations.SelectMany(scene => scene.PngFrames)
            .Any(frame => frame.DurationSeconds is < 0.01 or > 60))
        {
            errors.Add(LocalizationService.Get("Validation.FrameDuration"));
        }
        if (companion.Animations.Any(scene =>
                scene.FrameLoopMode != AnimationFrameLoopMode.EntireAnimation
                && scene.PngFrames.Count > 0
                && !scene.TryGetLoopRange(scene.PngFrames.Count, out _, out _)))
        {
            errors.Add(LocalizationService.Get("Validation.FrameLoopRange"));
        }
        if (companion.Animations.Any(scene =>
                scene.PngFrames.Count > 0
                && (scene.DialogStartFrameIndex < 0
                    || scene.DialogStartFrameIndex > scene.GetLastFrameReachableBeforeLoopRepeats(scene.PngFrames.Count))))
        {
            errors.Add(LocalizationService.Get("Validation.DialogStartFrame"));
        }
        if (companion.IdleDefaultAnimationId is null || companion.IdleDefaultAnimation is null)
            errors.Add(LocalizationService.Get("Validation.IdleDefaultAnimation"));
        if (companion.StartAnimationId is not null && companion.StartAnimation is null)
            errors.Add(LocalizationService.Get("Validation.StartAnimation"));
        var optionalStateIds = new Guid?[]
        {
            companion.ReminderAnimationId,
            companion.SelfBirthdayAnimation1Id,
            companion.SelfBirthdayAnimation2Id,
            companion.UserBirthdayAnimation1Id,
            companion.UserBirthdayAnimation2Id,
            companion.ExitAnimationId
        };
        if (optionalStateIds.Any(id => id is not null && companion.FindAnimation(id) is null))
            errors.Add(LocalizationService.Get("Validation.OptionalStateAnimation"));
        if (companion.InteractionStates is not null
            && Enum.GetValues<CompanionInteractionState>()
                .Select(companion.InteractionStates.GetAnimationId)
                .Any(id => id is not null && companion.FindAnimation(id) is null))
        {
            errors.Add(LocalizationService.Get("Validation.InteractionStateAnimation"));
        }
        if (companion.InteractionStates?.ClickAreas is not null
            && companion.InteractionStates.ClickAreas.Any(area =>
                area.AnimationId is not null && companion.FindAnimation(area.AnimationId) is null))
        {
            errors.Add(LocalizationService.Get("Validation.InteractionStateAnimation"));
        }
        if (companion.IdleRandomAnimationIds.Any(id => companion.FindAnimation(id) is null))
            errors.Add(LocalizationService.Get("Validation.IdleRandomAnimation"));
        if (companion.Animations.Any(scene => scene.DialogCharactersPerSecond is < 5 or > 40))
            errors.Add(LocalizationService.Get("Validation.DialogSpeed"));
        return errors;
    }

    private static bool HasValidAnimationSource(AnimationScene scene)
    {
        if (scene.PngFrames.Count > 0)
        {
            return scene.PngFrames.All(frame =>
                Path.GetExtension(frame.FilePath).Equals(".png", StringComparison.OrdinalIgnoreCase)
                && File.Exists(frame.FilePath));
        }

        return !string.IsNullOrWhiteSpace(scene.AnimationPath) && File.Exists(scene.AnimationPath);
    }
}
