using System.Text.Json.Serialization;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Models;

public sealed class CompanionDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = LocalizationService.Get("Model.Companion.New");
    public string Birthday { get; set; } = string.Empty;
    public string UserNickname { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string ThinkingPhrase { get; set; } = string.Empty;
    public string FontFamilyName { get; set; } = "Segoe UI";
    public string? ImportedFontPath { get; set; }
    public string FontColor { get; set; } = "#FFFFFFFF";
    public string? ProfilePicturePath { get; set; }
    public string? BubbleImagePath { get; set; }
    public List<AnimationScene> Animations { get; set; } = [];
    public Guid? StartAnimationId { get; set; }
    public Guid? ReminderAnimationId { get; set; }
    public Guid? SelfBirthdayAnimation1Id { get; set; }
    public Guid? SelfBirthdayAnimation2Id { get; set; }
    public Guid? UserBirthdayAnimation1Id { get; set; }
    public Guid? UserBirthdayAnimation2Id { get; set; }
    public Guid? ExitAnimationId { get; set; }
    public Guid? IdleDefaultAnimationId { get; set; }
    public List<Guid> IdleRandomAnimationIds { get; set; } = [];
    public CompanionInteractionStateAssignments InteractionStates { get; set; } = new();

    [JsonIgnore]
    public AnimationScene? StartAnimation => FindAnimation(StartAnimationId);

    [JsonIgnore]
    public AnimationScene? ReminderAnimation => FindAnimation(ReminderAnimationId);

    [JsonIgnore]
    public AnimationScene? SelfBirthdayAnimation1 => FindAnimation(SelfBirthdayAnimation1Id);

    [JsonIgnore]
    public AnimationScene? SelfBirthdayAnimation2 => FindAnimation(SelfBirthdayAnimation2Id);

    [JsonIgnore]
    public AnimationScene? UserBirthdayAnimation1 => FindAnimation(UserBirthdayAnimation1Id);

    [JsonIgnore]
    public AnimationScene? UserBirthdayAnimation2 => FindAnimation(UserBirthdayAnimation2Id);

    [JsonIgnore]
    public AnimationScene? ExitAnimation => FindAnimation(ExitAnimationId);

    [JsonIgnore]
    public AnimationScene? IdleDefaultAnimation => FindAnimation(IdleDefaultAnimationId);

    public AnimationScene? GetInteractionAnimation(CompanionInteractionState state) =>
        FindAnimation(InteractionStates?.GetAnimationId(state));

    public IEnumerable<string> GetDialogueExamples() => Animations
        .SelectMany(scene => scene.DialogLines)
        .Where(line => !string.IsNullOrWhiteSpace(line.Text))
        .Select(line => line.Text.Trim())
        .Distinct(StringComparer.Ordinal);

    public IEnumerable<string?> GetAssetPaths()
    {
        yield return ImportedFontPath;
        yield return ProfilePicturePath;
        yield return BubbleImagePath;
        foreach (var scene in Animations)
        {
            yield return scene.AnimationPath;
            foreach (var frame in scene.PngFrames)
                yield return frame.FilePath;
            foreach (var line in scene.DialogLines)
                yield return line.VoicePath;
        }
    }

    public CompanionDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        Birthday = Birthday,
        UserNickname = UserNickname,
        Description = Description ?? string.Empty,
        Personality = Personality ?? string.Empty,
        ThinkingPhrase = ThinkingPhrase ?? string.Empty,
        FontFamilyName = FontFamilyName,
        ImportedFontPath = ImportedFontPath,
        FontColor = FontColor,
        ProfilePicturePath = ProfilePicturePath,
        BubbleImagePath = BubbleImagePath,
        Animations = Animations.Select(scene => scene.Clone()).ToList(),
        StartAnimationId = StartAnimationId,
        ReminderAnimationId = ReminderAnimationId,
        SelfBirthdayAnimation1Id = SelfBirthdayAnimation1Id,
        SelfBirthdayAnimation2Id = SelfBirthdayAnimation2Id,
        UserBirthdayAnimation1Id = UserBirthdayAnimation1Id,
        UserBirthdayAnimation2Id = UserBirthdayAnimation2Id,
        ExitAnimationId = ExitAnimationId,
        IdleDefaultAnimationId = IdleDefaultAnimationId,
        IdleRandomAnimationIds = [.. IdleRandomAnimationIds],
        InteractionStates = InteractionStates?.Clone() ?? new()
    };

    public AnimationScene? FindAnimation(Guid? id) => id is Guid animationId
        ? Animations.FirstOrDefault(scene => scene.Id == animationId)
        : null;
}
